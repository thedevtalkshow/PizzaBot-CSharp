using Azure.AI.VoiceLive;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace PizzaBot.ConsumerWeb.Services;

/// <summary>
/// Bridges a single browser WebSocket connection to a Voice Live session.
///
/// Protocol (TEXT frames = JSON, BINARY frames = raw PCM16 audio):
///
///   Browser → Server (TEXT):
///     { "type": "sdp", "sdp": "v=0..." }         — WebRTC offer for avatar
///     { "type": "stop" }                          — end session
///
///   Browser → Server (BINARY):
///     raw PCM16, 24 kHz, mono                     — microphone audio
///
///   Server → Browser (TEXT):
///     { "type": "status",    "connected": true }  — session ready
///     { "type": "serverSdp", "sdp": "v=0..." }    — WebRTC answer for avatar
///     { "type": "transcript","role": "user"|"assistant", "text": "..." }
///     { "type": "error",     "message": "..." }
/// </summary>
public static class VoiceLiveWebSocketHandler
{
    public static async Task HandleAsync(
        WebSocket ws,
        VoiceLiveSessionService sessionService,
        ILogger logger,
        CancellationToken appStopping)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(appStopping);
        var ct = cts.Token;

        await using VoiceLiveSession? session = await CreateSessionSafe(ws, sessionService, logger, ct);
        if (session is null) return;

        await SendJsonAsync(ws, new { type = "status", connected = true }, ct);
        logger.LogInformation("[VoiceLive] Session started, waiting for browser SDP.");

        // WebSocket sends must be serialized — Voice Live events arrive on a background task
        var sendLock = new SemaphoreSlim(1, 1);

        // Task A: forward Voice Live events to the browser
        var eventsTask = ForwardEventsAsync(session, ws, sendLock, logger, ct);

        // Task B: receive from browser, forward to Voice Live
        await ReceiveFromBrowserAsync(session, ws, sendLock, logger, cts, ct);

        cts.Cancel(); // signal event loop to stop
        await eventsTask;

        if (ws.State == WebSocketState.Open)
            await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "session ended", CancellationToken.None);
    }

    // ── Session creation ──────────────────────────────────────────────────────

    private static async Task<VoiceLiveSession?> CreateSessionSafe(
        WebSocket ws, VoiceLiveSessionService sessionService, ILogger logger, CancellationToken ct)
    {
        try
        {
            return await sessionService.CreateSessionAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[VoiceLive] Failed to create session.");
            await SendJsonAsync(ws, new { type = "error", message = ex.Message }, ct);
            await ws.CloseAsync(WebSocketCloseStatus.InternalServerError, "session creation failed", CancellationToken.None);
            return null;
        }
    }

    // ── Voice Live → Browser ──────────────────────────────────────────────────

    private static async Task ForwardEventsAsync(
        VoiceLiveSession session, WebSocket ws, SemaphoreSlim sendLock, ILogger logger, CancellationToken ct)
    {
        try
        {
            await foreach (var update in session.GetUpdatesAsync(ct))
            {
                if (update is SessionUpdateAvatarConnecting avatarConnecting)
                {
                    var serverSdp = avatarConnecting.ServerSdp ?? string.Empty;
                    logger.LogInformation("[VoiceLive] SessionUpdateAvatarConnecting — ServerSdp length={Length}, prefix={Prefix}",
                        serverSdp.Length, serverSdp[..Math.Min(30, serverSdp.Length)]);

                    // Voice Live returns ServerSdp as btoa(JSON.stringify({type:"answer",sdp:"..."})) already —
                    // pass it through directly; the browser does JSON.parse(atob(sdp)) to get RTCSessionDescription.
                    logger.LogInformation("[VoiceLive] Sending serverSdp to browser (length={Length}).", serverSdp.Length);
                    await SendJsonLockedAsync(ws, sendLock, new { type = "serverSdp", sdp = serverSdp }, ct);
                }
                else if (update is SessionUpdateConversationItemInputAudioTranscriptionCompleted userTranscript)
                {
                    // What the user said (STT result)
                    await SendJsonLockedAsync(ws, sendLock, new { type = "transcript", role = "user", text = userTranscript.Transcript }, ct);
                }
                else if (update is SessionUpdateResponseAudioTranscriptDone assistantTranscript)
                {
                    // What the assistant said (TTS text, final)
                    await SendJsonLockedAsync(ws, sendLock, new { type = "transcript", role = "assistant", text = assistantTranscript.Transcript }, ct);
                }
                else if (update is SessionUpdateError error)
                {
                    var msg = error.Error?.Message ?? "unknown error";
                    logger.LogWarning("[VoiceLive] Session error: {Message}", msg);
                    await SendJsonLockedAsync(ws, sendLock, new { type = "error", message = msg }, ct);
                }
                else
                {
                    logger.LogDebug("[VoiceLive] Unhandled update type: {Type}", update.GetType().Name);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ws.State == WebSocketState.Open)
        {
            logger.LogError(ex, "[VoiceLive] Error in event forwarding loop.");
        }
    }

    // ── Browser → Voice Live ──────────────────────────────────────────────────

    private static async Task ReceiveFromBrowserAsync(
        VoiceLiveSession session, WebSocket ws, SemaphoreSlim sendLock,
        ILogger logger, CancellationTokenSource cts, CancellationToken ct)
    {
        var buffer = new byte[64 * 1024];

        try
        {
            while (!ws.CloseStatus.HasValue && !ct.IsCancellationRequested)
            {
                var result = await ws.ReceiveAsync(buffer, ct);

                if (result.MessageType == WebSocketMessageType.Close) break;

                if (result.MessageType == WebSocketMessageType.Binary)
                {
                    // Raw PCM16 mic audio from the browser
                    var audio = new BinaryData(buffer.AsMemory(0, result.Count).ToArray());
                    await session.SendInputAudioAsync(audio, ct);
                }
                else if (result.MessageType == WebSocketMessageType.Text)
                {
                    await HandleTextMessageAsync(session, ws, sendLock, buffer, result.Count, logger, ct);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException ex)
        {
            logger.LogWarning("[VoiceLive] WebSocket closed unexpectedly: {Message}", ex.Message);
        }
    }

    private static async Task HandleTextMessageAsync(
        VoiceLiveSession session, WebSocket ws, SemaphoreSlim sendLock,
        byte[] buffer, int count, ILogger logger, CancellationToken ct)
    {
        var json = Encoding.UTF8.GetString(buffer, 0, count);
        using var doc = JsonDocument.Parse(json);
        var type = doc.RootElement.GetProperty("type").GetString();

        switch (type)
        {
            case "sdp":
                var sdp = doc.RootElement.GetProperty("sdp").GetString()
                    ?? throw new InvalidOperationException("SDP offer is missing.");
                logger.LogInformation("[VoiceLive] Received SDP offer from browser (length={Length}, prefix={Prefix}), calling ConnectAvatarAsync.",
                    sdp.Length, sdp[..Math.Min(20, sdp.Length)]);
                // Relay the browser's SDP offer to Voice Live; the service will respond with SessionUpdateAvatarConnecting
                await session.ConnectAvatarAsync(sdp, ct);
                logger.LogInformation("[VoiceLive] ConnectAvatarAsync returned — waiting for SessionUpdateAvatarConnecting.");
                break;

            case "stop":
                logger.LogInformation("[VoiceLive] Browser requested session stop.");
                break;

            default:
                logger.LogDebug("[VoiceLive] Unknown message type: {Type}", type);
                break;
        }
    }

    // ── WebSocket send helpers ────────────────────────────────────────────────

    private static async Task SendJsonAsync(WebSocket ws, object payload, CancellationToken ct)
    {
        if (ws.State != WebSocketState.Open) return;
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        await ws.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, ct);
    }

    private static async Task SendJsonLockedAsync(WebSocket ws, SemaphoreSlim sendLock, object payload, CancellationToken ct)
    {
        if (ws.State != WebSocketState.Open) return;
        await sendLock.WaitAsync(ct);
        try { await SendJsonAsync(ws, payload, ct); }
        finally { sendLock.Release(); }
    }
}
