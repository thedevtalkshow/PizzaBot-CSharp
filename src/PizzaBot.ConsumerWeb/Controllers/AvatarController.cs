using Microsoft.AspNetCore.Mvc;
using Microsoft.CognitiveServices.Speech;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PizzaBot.ConsumerWeb.Models;
using PizzaBot.ConsumerWeb.Services;
using System.Text;
using System.Web;

namespace PizzaBot.ConsumerWeb.Controllers;

[ApiController]
public class AvatarController(
    IOptions<AvatarSettings> avatarSettings,
    IClientService clientService,
    FoundryAgentService agentService) : ControllerBase
{
    private readonly AvatarSettings _settings = avatarSettings.Value;

    // ── Token endpoints ────────────────────────────────────────────────────────

    [HttpGet("api/getSpeechToken")]
    public IActionResult GetSpeechToken()
    {
        var token = GlobalAvatarVariables.SpeechToken;
        Console.WriteLine($"[GetSpeechToken] Region: {_settings.SpeechRegion}, token present: {token is not null}, prefix: {token?[..Math.Min(10, token?.Length ?? 0)]}");

        Response.Headers["SpeechRegion"] = _settings.SpeechRegion;
        if (!string.IsNullOrEmpty(_settings.SpeechPrivateEndpoint))
            Response.Headers["SpeechPrivateEndpoint"] = _settings.SpeechPrivateEndpoint;

        return Content(token ?? string.Empty, "text/plain");
    }

    [HttpGet("api/getIceToken")]
    public IActionResult GetIceToken()
    {
        if (!string.IsNullOrEmpty(_settings.IceServerUrl) &&
            !string.IsNullOrEmpty(_settings.IceServerUsername) &&
            !string.IsNullOrEmpty(_settings.IceServerPassword))
        {
            var custom = new
            {
                Urls = new[] { _settings.IceServerUrl },
                Username = _settings.IceServerUsername,
                Password = _settings.IceServerPassword
            };
            return Content(JsonConvert.SerializeObject(custom), "application/json");
        }

        return Content(GlobalAvatarVariables.IceToken ?? string.Empty, "application/json");
    }

    // ── Session lifecycle ──────────────────────────────────────────────────────

    [HttpGet("api/initializeClient")]
    public IActionResult InitializeClient()
    {
        var clientId = clientService.InitializeClient();
        // Initialize the Foundry agent session — use the clientId as the ordering userId
        agentService.InitializeSession(clientId, clientId.ToString("N"));
        return Ok(new { ClientId = clientId });
    }

    [HttpGet("api/getStatus")]
    public IActionResult GetStatus()
    {
        if (!TryGetClientId(out var clientId)) return BadRequest("Invalid ClientId");
        var ctx = clientService.GetClientContext(clientId);
        return Ok(JsonConvert.SerializeObject(new { speechSynthesizerConnected = ctx.SpeechSynthesizerConnected }));
    }

    [HttpPost("api/releaseClient")]
    public async Task<IActionResult> ReleaseClient()
    {
        string body;
        using var reader = new StreamReader(Request.Body, Encoding.UTF8);
        body = await reader.ReadToEndAsync();

        if (!Guid.TryParse(JObject.Parse(body).Value<string>("clientId"), out var clientId))
            return BadRequest("Invalid ClientId");

        await DisconnectAvatarInternal(clientId, isReconnecting: false);
        await Task.Delay(2000);
        clientService.RemoveClient(clientId);
        agentService.RemoveSession(clientId);
        return Ok($"Client context released for {clientId}.");
    }

    // ── Avatar connection ──────────────────────────────────────────────────────

    [HttpPost("api/connectAvatar")]
    public async Task<IActionResult> ConnectAvatar()
    {
        if (!TryGetClientId(out var clientId)) return BadRequest("Invalid ClientId");
        var ctx = clientService.GetClientContext(clientId);

        bool isReconnecting = Request.Headers.TryGetValue("Reconnect", out var reconnectHeader) &&
                              string.Equals(reconnectHeader, "true", StringComparison.OrdinalIgnoreCase);

        await DisconnectAvatarInternal(clientId, isReconnecting);

        // Initialize the Foundry agent session on first connect (not on reconnects, to preserve conversation history)
        if (!isReconnecting)
            agentService.InitializeSession(clientId, clientId.ToString("N"));

        ctx.TtsVoice = Request.Headers["TtsVoice"].FirstOrDefault() ?? _settings.TtsVoice;
        ctx.CustomVoiceEndpointId = Request.Headers["CustomVoiceEndpointId"].FirstOrDefault();
        ctx.PersonalVoiceSpeakerProfileId = Request.Headers["PersonalVoiceSpeakerProfileId"].FirstOrDefault();

        var customVoiceEndpointId = ctx.CustomVoiceEndpointId;
        var isCustomAvatar = string.Equals(Request.Headers["IsCustomAvatar"].FirstOrDefault(), "true", StringComparison.OrdinalIgnoreCase);
        var isCustomVoice = !string.IsNullOrEmpty(customVoiceEndpointId);
        var endpointRoute = isCustomAvatar || isCustomVoice ? "voice" : "tts";

        SpeechConfig speechConfig;
        if (!string.IsNullOrEmpty(_settings.SpeechPrivateEndpoint))
        {
            var wss = _settings.SpeechPrivateEndpoint.TrimEnd('/').Replace("https://", "wss://");
            speechConfig = SpeechConfig.FromEndpoint(
                new Uri($"{wss}/{endpointRoute}/cognitiveservices/websocket/v1?enableTalkingAvatar=true"),
                _settings.SpeechKey);
        }
        else
        {
            speechConfig = SpeechConfig.FromEndpoint(
                new Uri($"wss://{_settings.SpeechRegion}.{endpointRoute}.speech.microsoft.com/cognitiveservices/websocket/v1?enableTalkingAvatar=true"),
                _settings.SpeechKey);
        }

        if (!string.IsNullOrEmpty(customVoiceEndpointId))
            speechConfig.EndpointId = customVoiceEndpointId;

        var synthesizer = new SpeechSynthesizer(speechConfig, null);
        ctx.SpeechSynthesizer = synthesizer;

        if (string.IsNullOrEmpty(GlobalAvatarVariables.IceToken))
            return BadRequest("ICE token is not ready yet. Please wait a moment and try again.");

        var iceTokenObj = JsonConvert.DeserializeObject<Dictionary<string, object>>(GlobalAvatarVariables.IceToken);

        if (!string.IsNullOrEmpty(_settings.IceServerUrl) && !string.IsNullOrEmpty(_settings.IceServerUsername) && !string.IsNullOrEmpty(_settings.IceServerPassword))
        {
            iceTokenObj = new Dictionary<string, object>
            {
                { "Urls", string.IsNullOrEmpty(_settings.IceServerUrlRemote) ? new JArray(_settings.IceServerUrl) : new JArray(_settings.IceServerUrlRemote) },
                { "Username", _settings.IceServerUsername },
                { "Password", _settings.IceServerPassword }
            };
        }

        string localSdp;
        using (var reader = new StreamReader(Request.Body, Encoding.UTF8))
            localSdp = await reader.ReadToEndAsync();

        var avatarCharacter = Request.Headers["AvatarCharacter"].FirstOrDefault() ?? _settings.AvatarCharacter;
        var avatarStyle = Request.Headers["AvatarStyle"].FirstOrDefault() ?? _settings.AvatarStyle;
        var backgroundColor = Request.Headers["BackgroundColor"].FirstOrDefault() ?? _settings.BackgroundColor;
        var backgroundImageUrl = Request.Headers["BackgroundImageUrl"].FirstOrDefault();
        var transparentBackground = string.Equals(Request.Headers["TransparentBackground"].FirstOrDefault(), "true", StringComparison.OrdinalIgnoreCase);
        var videoCrop = string.Equals(Request.Headers["VideoCrop"].FirstOrDefault(), "true", StringComparison.OrdinalIgnoreCase);

        var urlsArray = iceTokenObj?.TryGetValue("Urls", out var urlVal) == true ? urlVal as JArray : null;
        var firstUrl = urlsArray?.FirstOrDefault()?.ToString();

        var avatarConfig = new
        {
            synthesis = new
            {
                video = new
                {
                    protocol = new
                    {
                        name = "WebRTC",
                        webrtcConfig = new
                        {
                            clientDescription = localSdp,
                            iceServers = new[]
                            {
                                new
                                {
                                    urls = new[] { firstUrl },
                                    username = iceTokenObj!["Username"],
                                    credential = iceTokenObj["Password"]
                                }
                            },
                            auditAudio = AvatarSettings.EnableAudioAudit
                        }
                    },
                    format = new
                    {
                        resolution = new { width = 1920, height = 1080 },
                        crop = new
                        {
                            topLeft = new { x = videoCrop ? 600 : 0, y = 0 },
                            bottomRight = new { x = videoCrop ? 1320 : 1920, y = 1080 }
                        },
                        bitrate = 1000000
                    },
                    talkingAvatar = new
                    {
                        photoAvatarBaseModel = string.Empty,
                        customized = isCustomAvatar,
                        character = avatarCharacter,
                        style = avatarStyle,
                        background = new
                        {
                            color = transparentBackground ? "#00FF00FF" : backgroundColor,
                            image = new { url = backgroundImageUrl }
                        },
                        scene = new { zoom = 1.0, positionX = 0.0, positionY = 0.0, rotationX = 0.0, rotationY = 0.0, rotationZ = 0.0, amplitude = 1.0 }
                    }
                }
            }
        };

        var connection = Connection.FromSpeechSynthesizer(synthesizer);
        connection.SetMessageProperty("speech.config", "context", JsonConvert.SerializeObject(avatarConfig));

        connection.Connected += (_, _) => Console.WriteLine("[Avatar] TTS service connected.");
        connection.Disconnected += (_, _) =>
        {
            Console.WriteLine("[Avatar] TTS service disconnected.");
            ctx.SpeechSynthesizerConnection = null;
            ctx.SpeechSynthesizerConnected = false;
        };

        ctx.SpeechSynthesizerConnection = connection;
        ctx.SpeechSynthesizerConnected = true;

        var result = synthesizer.SpeakTextAsync("").Result;
        if (result.Reason == ResultReason.Canceled)
        {
            var details = SpeechSynthesisCancellationDetails.FromResult(result);
            throw new Exception(details.ErrorDetails);
        }

        var turnStartJson = synthesizer.Properties.GetProperty("SpeechSDKInternal-ExtraTurnStartMessage");
        var remoteSdp = JsonConvert.DeserializeObject<JObject>(turnStartJson)?["webrtc"]?["connectionString"]?.ToString() ?? string.Empty;
        return Content(remoteSdp, "application/json");
    }

    [HttpPost("api/disconnectAvatar")]
    public async Task<IActionResult> DisconnectAvatar()
    {
        if (!TryGetClientId(out var clientId)) return BadRequest("Invalid ClientId");
        await DisconnectAvatarInternal(clientId, isReconnecting: false);
        return Ok("Disconnected.");
    }

    // ── Chat ───────────────────────────────────────────────────────────────────

    [HttpPost("api/chat")]
    public async Task<IActionResult> Chat()
    {
        if (!TryGetClientId(out var clientId)) return BadRequest("Invalid or missing ClientId.");

        string userQuery;
        using (var reader = new StreamReader(Request.Body, Encoding.UTF8))
            userQuery = await reader.ReadToEndAsync();

        await HandleUserQuery(userQuery, clientId, Response);
        return new EmptyResult();
    }

    [HttpPost("api/chat/continueSpeaking")]
    public IActionResult ContinueSpeaking()
    {
        if (!TryGetClientId(out var clientId)) return BadRequest("Invalid ClientId");
        var ctx = clientService.GetClientContext(clientId);

        if (!string.IsNullOrEmpty(ctx.SpeakingText) && AvatarSettings.RepeatSpeakingSentenceAfterReconnection)
            ctx.SpokenTextQueue.AddFirst(ctx.SpeakingText);

        if (ctx.SpokenTextQueue.Count > 0)
            SpeakWithQueue(null!, 0, clientId, null);

        return Ok("Request sent.");
    }

    [HttpPost("api/chat/clearHistory")]
    public IActionResult ClearChatHistory()
    {
        if (!TryGetClientId(out var clientId)) return BadRequest("Invalid ClientId");
        // Re-initialize the agent session to clear conversation history
        agentService.RemoveSession(clientId);
        agentService.InitializeSession(clientId, clientId.ToString("N"));
        return Ok("Chat history cleared.");
    }

    [HttpPost("api/speak")]
    public async Task<IActionResult> Speak()
    {
        if (!TryGetClientId(out var clientId)) return BadRequest("Invalid ClientId");

        string ssml;
        using (var reader = new StreamReader(Request.Body, Encoding.UTF8))
            ssml = await reader.ReadToEndAsync();

        var resultId = await SpeakSsml(ssml, clientId);
        return Content(resultId, "text/plain");
    }

    [HttpPost("api/stopSpeaking")]
    public async Task<IActionResult> StopSpeaking()
    {
        if (!TryGetClientId(out var clientId)) return BadRequest("Invalid ClientId");
        await StopSpeakingInternal(clientId, skipClearingQueue: false);
        return Ok("Speaking stopped.");
    }

    // ── Internal helpers ───────────────────────────────────────────────────────

    private async Task HandleUserQuery(string userQuery, Guid clientId, HttpResponse httpResponse)
    {
        // Get the full response from the Foundry agent (includes all tool calls)
        var agentStartTime = DateTime.Now;
        var agentResponse = await Task.Run(() => agentService.SendMessage(clientId, userQuery));
        var agentLatency = (int)(DateTime.Now.Subtract(agentStartTime).TotalMilliseconds + 0.5);
        Console.WriteLine($"[Agent] Response in {agentLatency}ms");

        // Stream timing markers to client, then the full response text
        await httpResponse.WriteAsync($"<FTL>{agentLatency}</FTL>");
        await httpResponse.WriteAsync($"<FSL>{agentLatency}</FSL>");
        await httpResponse.WriteAsync(agentResponse);

        // Split response into sentences and queue for TTS
        var sentences = SplitIntoSentences(agentResponse);
        foreach (var sentence in sentences)
        {
            if (!string.IsNullOrWhiteSpace(sentence))
                await SpeakWithQueue(sentence, 0, clientId, httpResponse);
        }
    }

    /// <summary>
    /// Splits a block of text into sentences at punctuation boundaries for TTS queuing.
    /// </summary>
    private static List<string> SplitIntoSentences(string text)
    {
        var sentences = new List<string>();
        var current = new StringBuilder();

        foreach (var ch in text)
        {
            current.Append(ch);
            if (AvatarSettings.SentenceLevelPunctuations.Any(p => p[0] == ch))
            {
                var s = current.ToString().Trim();
                if (s.Length > 0) sentences.Add(s);
                current.Clear();
            }
        }

        var remaining = current.ToString().Trim();
        if (remaining.Length > 0) sentences.Add(remaining);

        return sentences;
    }

    private Task SpeakWithQueue(string text, int endingSilenceMs, Guid clientId, HttpResponse? httpResponse)
    {
        var ctx = clientService.GetClientContext(clientId);
        ctx.SpokenTextQueue.AddLast(text);

        if (!ctx.IsSpeaking)
        {
            ctx.IsSpeaking = true;

            _ = Task.Run(async () =>
            {
                try
                {
                    while (ctx.SpokenTextQueue.Count > 0)
                    {
                        var current = ctx.SpokenTextQueue.First!.Value;
                        ctx.SpeakingText = current;
                        ctx.SpokenTextQueue.RemoveFirst();
                        await SpeakText(current, ctx.TtsVoice, ctx.PersonalVoiceSpeakerProfileId ?? string.Empty, endingSilenceMs, clientId);
                        ctx.LastSpeakTime = DateTime.UtcNow;
                    }
                }
                finally
                {
                    ctx.IsSpeaking = false;
                    ctx.SpeakingText = null;
                }
            });
        }

        return Task.CompletedTask;
    }

    private async Task<string> SpeakText(string text, string voice, string speakerProfileId, int endingSilenceMs, Guid clientId)
    {
        var escaped = HttpUtility.HtmlEncode(text);
        var silence = endingSilenceMs > 0 ? $"<break time='{endingSilenceMs}ms' />" : string.Empty;

        var ssml = $"""
            <speak version='1.0' xmlns='http://www.w3.org/2001/10/synthesis' xmlns:mstts='http://www.w3.org/2001/mstts' xml:lang='en-US'>
                <voice name='{voice}'>
                    <mstts:ttsembedding speakerProfileId='{speakerProfileId}'>
                        <mstts:leadingsilence-exact value='0'/>
                        {escaped}{silence}
                    </mstts:ttsembedding>
                </voice>
            </speak>
            """;

        return await SpeakSsml(ssml, clientId);
    }

    private async Task<string> SpeakSsml(string ssml, Guid clientId)
    {
        var ctx = clientService.GetClientContext(clientId);
        if (ctx.SpeechSynthesizer is not SpeechSynthesizer synthesizer)
            throw new InvalidOperationException("SpeechSynthesizer is not initialized.");

        var result = await synthesizer.SpeakSsmlAsync(ssml);
        if (result.Reason == ResultReason.Canceled)
        {
            var details = SpeechSynthesisCancellationDetails.FromResult(result);
            if (details.Reason == CancellationReason.Error)
                throw new Exception(details.ErrorDetails);
        }

        return result.ResultId;
    }

    private async Task StopSpeakingInternal(Guid clientId, bool skipClearingQueue)
    {
        var ctx = clientService.GetClientContext(clientId);
        ctx.IsSpeaking = false;

        if (!skipClearingQueue)
            ctx.SpokenTextQueue.Clear();

        if (ctx.SpeechSynthesizerConnection is Connection conn)
        {
            try { await conn.SendMessageAsync("synthesis.control", "{\"action\":\"stop\"}"); }
            catch (Exception ex) { Console.WriteLine($"[Avatar] StopSpeaking error: {ex.Message}"); }
        }
    }

    private async Task DisconnectAvatarInternal(Guid clientId, bool isReconnecting)
    {
        await StopSpeakingInternal(clientId, skipClearingQueue: isReconnecting);
        await Task.Delay(2000);

        var ctx = clientService.GetClientContext(clientId);
        if (ctx.SpeechSynthesizerConnection is Connection conn)
            conn.Close();
    }

    private bool TryGetClientId(out Guid clientId)
    {
        var header = Request.Headers["ClientId"].FirstOrDefault();
        return Guid.TryParse(header, out clientId);
    }
}
