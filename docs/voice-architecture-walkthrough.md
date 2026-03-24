# Talking Avatar Architecture Walkthrough

> **For The Dev Talk Show / presentations**
>
> This doc walks through two approaches to building a talking avatar pizza ordering experience in
> ASP.NET Core. Each approach uses Azure AI, but the amount of code you write — and what Azure
> manages on your behalf — is very different. The same feature, two very different paths.

---

## What We're Building

A web app where a user can speak to an AI avatar. The avatar:
- Listens to the user via microphone
- Understands what they're asking (pizza order, questions, etc.)
- Responds with a synthesised voice, lip-syncing on a video stream

Behind the scenes that means we need:

| Capability | Description |
|---|---|
| **Speech-to-Text (STT)** | Turn mic audio into text |
| **AI Agent** | Process the text, call tools, generate a response |
| **Text-to-Speech (TTS)** | Turn the response text back into audio |
| **Talking Avatar** | Lip-sync video stream driven by the audio |
| **WebRTC** | Stream the avatar video + audio to the browser |

Both paths in this repo do all five things. The question is: who writes the glue?

---

## Path 1: Manual Orchestration

> **Route:** `/`  
> **Key file:** `AvatarController.cs`  
> **SDK:** `Microsoft.CognitiveServices.Speech`

In Path 1 you own every step of the pipeline. You call each Azure service directly, manage their
tokens, wire their outputs into each other's inputs, and clean up after yourself.

### Step 1 — Background token management

The Speech SDK and WebRTC both need short-lived credentials. Before any user interaction, two
background services start refreshing tokens every few minutes and storing them in a singleton:

```csharp
// SpeechTokenService.cs
var token = await credential.GetTokenAsync(
    new TokenRequestContext(["https://cognitiveservices.azure.com/.default"]));
GlobalAvatarVariables.SpeechToken = $"aad#{_settings.SpeechResourceUrl}#{token.Token}";
```

```csharp
// IceTokenService.cs — fetches TURN server credentials for WebRTC
var response = await httpClient.GetAsync(iceTokenEndpoint);
GlobalAvatarVariables.IceToken = await response.Content.ReadFromJsonAsync<IceToken>();
```

The `aad#` prefix is a Speech SDK convention: it tells the SDK "this is an Entra ID token for
this resource, not a subscription key." Without this prefix, the SDK treats the value as an
API key and auth fails.

> **Teaching moment:** ICE (Interactive Connectivity Establishment) is the WebRTC protocol for
> figuring out how two peers — a browser and Azure — can reach each other through NAT and
> firewalls. TURN servers act as relays when a direct connection isn't possible.

---

### Step 2 — WebRTC peer connection (browser)

When the page loads, the browser immediately sets up a `RTCPeerConnection`. This is the object
that will eventually receive the avatar's video and audio:

```javascript
// chat.js
peerConnection = new RTCPeerConnection({ iceServers, iceTransportPolicy: 'relay' });
peerConnection.addTransceiver('video', { direction: 'recvonly' });
peerConnection.addTransceiver('audio', { direction: 'recvonly' });

peerConnection.ontrack = e => {
    if (e.track.kind === 'video') remoteVideoDiv.appendChild(videoElem);
    if (e.track.kind === 'audio') audioElem.srcObject = e.streams[0];
};
```

The browser generates an SDP offer (a description of its media capabilities and ICE candidates),
waits for ICE gathering to complete, then base64-encodes it to send to the server:

```javascript
const localSdp = btoa(JSON.stringify(peerConnection.localDescription));
```

> **Teaching moment:** SDP (Session Description Protocol) is the negotiation format WebRTC uses.
> Both sides exchange SDPs — the browser says "here's what I can receive," Azure says "here's
> what I'll send." After that exchange the media stream can flow.

---

### Step 3 — Avatar session connection (server)

When the user clicks Start, the browser POSTs its SDP offer to `/api/connectAvatar`. The server:

1. Creates a `SpeechConfig` pointing at the Azure Speech endpoint
2. Creates a `SpeechSynthesizer` (TTS)
3. Builds an avatar config payload: character, style, background, WebRTC config
4. Sends a silent `SpeakTextAsync("")` — this tricks the SDK into triggering the WebRTC handshake
5. Reads the SDP answer back out of an internal SDK event message
6. Returns the base64-encoded SDP answer to the browser

```csharp
// AvatarController.cs — the avatar config that drives WebRTC negotiation
var avatarConfig = new {
    synthesis = new {
        video = new {
            protocol = new {
                name = "WebRTC",
                webrtcConfig = new {
                    clientDescription = localSdp,  // browser's SDP offer
                    iceServers = new[] { new { urls, username, credential } }
                }
            },
            avatar = new { character, style, background }
        }
    }
};
```

```csharp
// The SDK sends this config to Azure, which returns a remote SDP answer
await synthesizer.SpeakTextAsync("");
var remoteSdp = JsonConvert.DeserializeObject<JObject>(turnStartJson)
    ?["webrtc"]?["connectionString"]?.ToString();
```

> **Teaching moment:** That `SpeakTextAsync("")` call is effectively a no-op for audio — it's
> just the mechanism the Speech SDK uses to trigger the WebRTC session setup. The real payload
> is in the avatar config you attached to the connection beforehand.

---

### Step 4 — STT in the browser

The user's microphone is handled by the Speech SDK running **in the browser**. You load the
Speech SDK bundle, create a `SpeechRecognizer`, and subscribe to recognition events:

```javascript
// chat.js
const speechRecognizer = new SpeechSDK.SpeechRecognizer(speechConfig, audioConfig);

speechRecognizer.recognized = (s, e) => {
    if (e.result.reason === SpeechSDK.ResultReason.RecognizedSpeech) {
        handleUserQuery(e.result.text);
    }
};

speechRecognizer.startContinuousRecognitionAsync();
```

The browser SDK opens its own WebSocket directly to Azure Speech — the server never sees the raw
audio. It only receives the transcribed text.

---

### Step 5 — The AI agent

The transcribed text goes to the server via POST `/api/chat`. The server calls the Foundry agent:

```csharp
// FoundryAgentService.cs
var response = await client.Runs.CreateRunAsync(threadId, agentId, new CreateRunOptions());

// Agent may call tools — handle each one
await foreach (var streamEvent in response) {
    if (streamEvent is RunStepDeltaUpdate delta) { /* function call in progress */ }
    if (streamEvent is RequiredActionUpdate action) {
        // Execute the tool client-side, submit result back
        var result = CalculateNumberOfPizzasToOrder(action.FunctionName, action.FunctionArguments);
        await client.Runs.SubmitToolOutputsToRunAsync(threadId, runId, toolOutputs);
    }
}
```

The agent can call a `CalculateNumberOfPizzasToOrder` function — the server runs it locally and
feeds the result back into the conversation before the agent generates its final text.

---

### Step 6 — TTS and avatar speaking

The agent's text response is split into sentences and queued for speech synthesis:

```csharp
// AvatarController.cs
var sentences = SplitTextIntoSentences(agentResponse);
foreach (var sentence in sentences)
    await SpeakWithQueue(clientId, sentence);

// SpeakText generates SSML and calls the synthesizer
var ssml = $@"<speak version='1.0'>
    <voice name='{_settings.TtsVoice}'>
        <mstts:ttsembedding speakerProfileId='{speakerProfileId}'>
            {sentence}
        </mstts:ttsembedding>
    </voice>
</speak>";

await synthesizer.SpeakSsmlAsync(ssml);
```

The Speech SDK pushes audio frames directly into the WebRTC connection. The avatar service on
Azure receives those frames, generates the lip-sync video, and streams both back to the browser
via the peer connection established in Step 3.

---

### Path 1 — What you end up with

```
Services/
  ├─ SpeechTokenService.cs        background token refresh
  ├─ IceTokenService.cs           background TURN credential refresh
  ├─ FoundryAgentService.cs       conversation + tool calling
  └─ ClientService.cs             per-session state

Controllers/
  └─ AvatarController.cs          orchestrates everything above (~440 lines)

wwwroot/js/
  └─ chat.js                      browser SDK, WebRTC, microphone (~700 lines)
```

**Total: ~738 lines C# + ~700 lines JavaScript**  
**Config: 12+ settings** (region, keys, ICE URLs, TURN credentials, voice name, avatar character, background colour…)

You control everything. If something breaks, you can see exactly where. If you want a custom
voice, custom SSML prosody, latency metrics, or to swap out any piece — you can.

---

## Path 2: Azure AI Voice Live

> **Route:** `/voice-live`  
> **Key file:** `VoiceLiveWebSocketHandler.cs`  
> **SDK:** `Azure.AI.VoiceLive`

In Path 2 you describe what you want and Voice Live handles the orchestration. STT, the agent,
TTS, and avatar WebRTC are all managed inside a single service. You write the plumbing between
the browser and that service.

### Step 1 — Create a Voice Live session

One service, one call. You tell it which Foundry agent to use, what avatar character to show,
and how to stream the output:

```csharp
// VoiceLiveSessionService.cs
_client = new VoiceLiveClient(new Uri(endpoint), new DefaultAzureCredential());

var agentConfig = new AgentSessionConfig(_agentName, projectName);
var target = SessionTarget.FromAgent(agentConfig);

var options = new VoiceLiveSessionOptions
{
    Avatar = new AvatarConfiguration("lisa", customized: false)
    {
        Style = "casual-sitting",
        OutputProtocol = AvatarOutputProtocol.Webrtc,
    },
    InputAudioTranscription = new AudioInputTranscriptionOptions(
        AudioInputTranscriptionOptionsModel.AzureSpeech),
};

return await _client.StartSessionAsync(target, options);
```

That's the entire server-side setup. No token management, no synthesizer, no ICE fetching.
`DefaultAzureCredential` handles auth automatically.

---

### Step 2 — Bridge the browser over WebSocket

The browser can't talk to Voice Live directly (auth requires server-side credentials). So the
server acts as a bridge: a custom WebSocket endpoint at `/ws/voice-live`.

Two concurrent tasks run for the lifetime of a browser connection:

```csharp
// VoiceLiveWebSocketHandler.cs
// Task A: forward Voice Live events → browser
var eventsTask = ForwardEventsAsync(session, ws, sendLock, logger, ct);

// Task B: receive from browser → Voice Live
await ReceiveFromBrowserAsync(session, ws, sendLock, logger, cts, ct);
```

WebSocket sends must be serialised — Task A fires on a background thread, Task B runs on the
main handler. A `SemaphoreSlim(1,1)` lock ensures frames don't interleave:

```csharp
await sendLock.WaitAsync(ct);
try   { await ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct); }
finally { sendLock.Release(); }
```

---

### Step 3 — Receive events from Voice Live

Voice Live emits a stream of typed events. You pattern-match on the ones you care about:

```csharp
// VoiceLiveWebSocketHandler.cs
await foreach (var update in session.GetUpdatesAsync(ct))
{
    if (update is SessionUpdateAvatarConnecting avatarConnecting)
    {
        // Voice Live has its SDP answer ready — forward it to the browser
        await SendJsonLockedAsync(ws, sendLock,
            new { type = "serverSdp", sdp = avatarConnecting.ServerSdp }, ct);
    }
    else if (update is SessionUpdateConversationItemInputAudioTranscriptionCompleted u)
    {
        // STT result — show what the user said
        await SendJsonLockedAsync(ws, sendLock,
            new { type = "transcript", role = "user", text = u.Transcript }, ct);
    }
    else if (update is SessionUpdateResponseAudioTranscriptDone a)
    {
        // TTS result — show what the avatar said
        await SendJsonLockedAsync(ws, sendLock,
            new { type = "transcript", role = "assistant", text = a.Transcript }, ct);
    }
}
```

> **Teaching moment:** `ServerSdp` comes back already encoded in `btoa(JSON.stringify(...))` 
> format — the same base64-JSON convention Azure avatar services use. Pass it straight through;
> no re-encoding needed (we learned this the hard way — double-encoding it was the bug that
> broke WebRTC for several iterations).

---

### Step 4 — WebRTC in the browser

The browser still manages its own `RTCPeerConnection` — media flows directly between the
browser and the Voice Live avatar service, not through your server.

```javascript
// voice-live.js
const pc = new RTCPeerConnection({
    iceServers: [{ urls: 'stun:stun.l.google.com:19302' }]
});

pc.addTransceiver('video', { direction: 'sendrecv' });
pc.addTransceiver('audio', { direction: 'sendrecv' });

pc.ontrack = e => {
    if (e.track.kind === 'video') { /* attach to <video> element */ }
    if (e.track.kind === 'audio') { /* attach to <audio> element */ }
};
```

Once ICE gathering is complete, the browser sends its SDP offer to the server (over the
WebSocket), which relays it to Voice Live via `ConnectAvatarAsync()`. Voice Live responds with
its SDP answer, which flows back through the WebSocket to the browser:

```javascript
// Browser receives serverSdp from server, sets remote description
const sdpDesc = JSON.parse(atob(msg.sdp));
await pc.setRemoteDescription(new RTCSessionDescription(sdpDesc));
// WebRTC ICE checks now run — media stream arrives when connected
```

> **Teaching moment:** The server is only the *signalling* relay here. The actual media —
> avatar video and audio — flows directly between the browser and Azure's avatar service via
> WebRTC. The server never sees a single audio or video frame.

---

### Step 5 — Mic audio to the server

Without the browser Speech SDK, you capture raw microphone audio yourself using the Web Audio
API and send it as binary WebSocket frames:

```javascript
// voice-live.js
const audioContext = new AudioContext({ sampleRate: 24000 });
await audioContext.audioWorklet.addModule('js/pcm-processor.js');

const source = audioContext.createMediaStreamSource(micStream);
const worklet = new AudioWorkletNode(audioContext, 'pcm-processor');

worklet.port.onmessage = e => ws.send(e.data); // e.data is PCM16 Int16Array
source.connect(worklet);
```

```javascript
// pcm-processor.js — converts float32 (Web Audio default) to PCM16 (Voice Live input format)
_toInt16(float32) {
    const int16 = new Int16Array(float32.length);
    for (let i = 0; i < float32.length; i++) {
        const s = Math.max(-1, Math.min(1, float32[i]));
        int16[i] = s < 0 ? s * 32768 : s * 32767;
    }
    return int16.buffer;
}
```

The server receives the binary frames and forwards them into the Voice Live session:

```csharp
// VoiceLiveWebSocketHandler.cs
if (result.MessageType == WebSocketMessageType.Binary)
{
    var audio = new BinaryData(buffer.AsMemory(0, result.Count).ToArray());
    await session.SendInputAudioAsync(audio, ct);
}
```

> **Teaching moment:** Voice Live expects PCM16 mono at 24 kHz. The Web Audio API outputs
> float32. `pcm-processor.js` runs in an AudioWorklet (a separate audio thread) and does the
> conversion in real time, clamping values to avoid clipping. The buffer is transferred (not
> copied) to the main thread for efficiency.

---

### Path 2 — What you end up with

```
Services/
  ├─ VoiceLiveSessionService.cs       session factory (~60 lines)
  └─ VoiceLiveWebSocketHandler.cs     WebSocket bridge (~200 lines)

Models/
  └─ VoiceLiveSettings.cs             config (~20 lines)

wwwroot/js/
  ├─ voice-live.js                    browser WebRTC + mic (~300 lines)
  └─ pcm-processor.js                 AudioWorklet PCM converter (~50 lines)
```

**Total: ~280 lines C# + ~350 lines JavaScript**  
**Config: 4 settings** (`Endpoint`, `ProjectName`, `AvatarCharacter`, `AvatarStyle`)

---

## Side-by-Side Comparison

| | Path 1 (Manual) | Path 2 (Voice Live) |
|---|---|---|
| Server C# | ~738 lines | ~280 lines |
| Browser JS | ~700 lines | ~350 lines |
| Config settings | 12+ | 4 |
| Token management | Background refresh services | Transparent |
| STT | Browser Speech SDK | Voice Live server-side |
| Agent calls | Explicit per-utterance | Orchestrated by Voice Live |
| TTS | SSML via `SpeakSsmlAsync` | Voice Live pipeline |
| WebRTC ICE | Speech SDK TURN servers | Public STUN + Voice Live's own ICE |
| Custom SSML / voice | Full control | Service defaults |
| Latency metrics | Built-in (`<FTL>` / `<FSL>` markers) | Transcripts only |

---

## Which path for which problem?

**Use Path 1 when:**
- You need custom voices, SSML prosody control, or personal voice endpoints
- You want explicit latency instrumentation (first-token, first-sentence timing)
- You're integrating Speech SDK into something that already uses it
- You need to understand (or debug) every layer of the stack

**Use Path 2 when:**
- You want to ship quickly — 65% less code to write and maintain
- You don't need to own every layer — let Azure orchestrate STT → Agent → TTS → Avatar
- Your team is not a Speech SDK expert and you don't want to become one
- Simplicity and reliability matter more than maximum flexibility

---

## The big insight

Both paths do the same thing. The difference is the abstraction boundary.

Path 1 is what you'd have written in 2022 — before Voice Live existed. Every service is exposed,
every credential is your problem, every failure mode is yours to handle.

Path 2 is the same problem through a different lens: a unified real-time session API that treats
STT + Agent + TTS + Avatar as a single pipeline. You describe what you want, not how to do it.

The code you *don't* write in Path 2 didn't disappear — it's just running inside Azure instead
of inside your repo.
