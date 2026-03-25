# PizzaBot.ConsumerWeb

A Blazor Server web application that lets users order pizza by talking (or typing) to **Lisa**, an Azure AI avatar. Lisa is backed by the same **Azure AI Foundry** `ContosoPizzaBot` agent used in the console projects — the difference is that the conversation happens through a live talking avatar using the **Azure Speech Services Avatar API**, with speech recognition handled by the **Azure Cognitive Services Speech SDK** running in the browser. A live orders panel polls the Pizza API to show order status updates in real time.

---

## What It Demonstrates

**Step 1 — Blazor Server shell**
- Hosting a Blazor Server application with both SSR pages and `InteractiveServer` component islands
- Wiring up `IOptions<T>` configuration with the layered user secrets → environment variables → `appsettings.json` priority chain

**Step 2 — Avatar + Speech**
- Connecting to the [Azure Talking Avatar API](https://learn.microsoft.com/en-us/azure/ai-services/speech-service/text-to-speech-avatar/what-is-text-to-speech-avatar) over WebRTC to stream a live talking avatar video
- Fetching and refreshing Speech and ICE tokens server-side to keep browser credentials short-lived
- Integrating the [Azure Cognitive Services Speech SDK](https://learn.microsoft.com/en-us/azure/ai-services/speech-service/speech-sdk) in the browser for real-time speech-to-text recognition
- Bridging a vanilla JS SDK integration into a Blazor SSR page using hidden config inputs and a server-generated `clientId`

**Step 3 — Foundry agent**
- Connecting to an existing Azure AI Foundry agent and maintaining per-session conversation threads using `Azure.AI.Projects`
- Handling the `CalculateNumberOfPizzasToOrder` function tool call client-side, with all other tools (menu lookup, order placement) dispatched through the MCP server
- Injecting a user context message at session start so the agent uses the correct `userId` when calling the Pizza API

**Step 4 — Orders panel**
- Embedding a live Blazor `InteractiveServer` component island alongside an SSR page
- Polling `GET /api/orders` every 5 seconds to show orders with live status updates (`pending` → `in_preparation` → `ready` → `delivered`)
- Filtering orders by user ID with a client-side text input

---

## How It Works

### The clientId bridge

The SSR page and the browser JS SDK need to share a session identity. `Home.razor` generates a `clientId` at render time and writes it into a hidden input:

```csharp
// Home.razor — runs server-side at render time
@{
    var clientId = ClientService.InitializeClient(); // creates AvatarClientContext, stores in dict
}
<input type="hidden" id="clientId" value="@clientId" />
```

`chat.js` reads this on `window.onload` and sends it as a `ClientId` header on every API call. All server-side state (speech synthesizer, speak queue, Foundry conversation thread) is keyed on this `Guid`.

### Agent session initialization

The Foundry conversation thread is created when the WebRTC avatar connection is established — not at page load. This means the browser already has a valid `clientId` before the thread exists:

```csharp
// AvatarController.cs
[HttpPost("api/connectAvatar")]
public async Task<IActionResult> ConnectAvatar()
{
    // ...
    // Initialize on first connect; skip on reconnect to preserve conversation history
    if (!isReconnecting)
        agentService.InitializeSession(clientId, clientId.ToString("N"));
    // ...
}
```

`clientId.ToString("N")` (no hyphens) becomes the `userId` injected into the agent conversation, which the agent uses when calling `PlaceOrder` through the MCP server.

### The chat pipeline

User input (typed or spoken) flows through a single path:

```
browser (chat.js) → POST /api/chat → AvatarController.Chat()
    → FoundryAgentService.SendMessage()          // synchronous Foundry Responses API call
    → sentences split on punctuation
    → POST /api/speak (per sentence)             // TTS → avatar speaks over WebRTC
```

The response is split into sentences so Lisa starts speaking the first sentence while the rest of the response is still queued — avoiding a long pause before she starts talking.

### Ghost DOM elements

`chat.js` was adapted from the [Azure Cognitive Services Speech SDK avatar sample](https://github.com/Azure-Samples/cognitive-services-speech-sdk/tree/master/samples/csharp/web/avatar). The original sample has a full configuration form. In this app, those form inputs are replaced with hidden elements in `Home.razor` that hold hardcoded values:

```html
<!-- Feature flags — hardcoded for the pizza ordering experience -->
<input type="checkbox" id="continuousConversation" hidden checked />
<input type="checkbox" id="useLocalVideoForIdle" hidden />
<!-- ... -->
```

This keeps `chat.js` unchanged while removing the configuration UI entirely.

---

## Running the Sample

1. Ensure the `ContosoPizzaBot` agent exists in your Foundry project. Run `PizzaBot.CreateFoundryAgent` first if you haven't.
2. Set your configuration values (see below).
3. Authenticate: `az login`
4. Run:

```bash
dotnet run
```

5. Open the URL shown in the console (e.g. `https://localhost:5001`).
6. Click **Start Chat** — Lisa will appear after a few seconds as WebRTC connects.
7. Type a message or click **🎤 Speak** to use your microphone.

> **Ad blockers**: The Speech SDK JavaScript bundle is served from `wwwroot/js` to avoid CDN blocking. If you update the SDK version, replace `microsoft.cognitiveservices.speech.sdk.bundle.js` with the new bundle from the [Speech SDK releases](https://aka.ms/csspeech/jsbrowserpackageraw).

---

## Configuration

Settings are loaded in this priority order (highest wins):

1. **User secrets** (recommended for local development)
2. **Environment variables**
3. **`appsettings.json`**

### Included in this repo

| Key | Value |
|---|---|
| `Avatar:SpeechRegion` | `eastus` |
| `Avatar:TtsVoice` | `en-US-AvaNeural` |
| `Avatar:AvatarCharacter` | `lisa` |
| `Avatar:AvatarStyle` | `casual-sitting` |
| `PizzaBot:AgentName` | `ContosoPizzaBot` |

### You must supply

| Key | Description |
|---|---|
| `Avatar:SpeechKey` | Your Azure Speech Services API key |
| `PizzaBot:ProjectEndpoint` | Your Azure AI Foundry project endpoint, e.g. `https://<resource>.services.ai.azure.com/api/projects/<project>` |
| `PizzaApi:BaseUrl` | Base URL of the Pizza API, e.g. `https://<your-function-app>.azurewebsites.net` |

**Setting via user secrets** (run once from the project directory):

```bash
dotnet user-secrets set "Avatar:SpeechKey" "<your-speech-key>"
dotnet user-secrets set "PizzaBot:ProjectEndpoint" "https://<resource>.services.ai.azure.com/api/projects/<project>"
dotnet user-secrets set "PizzaApi:BaseUrl" "https://<your-function-app>.azurewebsites.net"
```

**Environment variables** use `__` as the section separator:

```bash
Avatar__SpeechKey=<your-speech-key>
PizzaBot__ProjectEndpoint=https://...
PizzaApi__BaseUrl=https://...
```

> ⚠️ **Never commit `Avatar:SpeechKey` or any other credential to source control.** Use user secrets or environment variables.

### Optional: Private endpoint / managed identity

If your Speech resource is behind a private endpoint or you want to use a managed identity instead of an API key, set:

| Key | Description |
|---|---|
| `Avatar:SpeechPrivateEndpoint` | Private endpoint URL, e.g. `https://<resource>.cognitiveservices.azure.com/` |
| `Avatar:SpeechResourceUrl` | Same URL, used to construct the AAD token audience |
| `Avatar:UserAssignedManagedIdentityClientId` | Client ID of the user-assigned managed identity (optional) |

> **Note:** Browser-side speech recognition (STT) cannot use a private endpoint — the Speech SDK JS running in the browser makes a WebSocket connection directly to the Speech service, which is blocked when a private endpoint is in use. Only the server-side TTS (avatar speech) supports private endpoints.

---

## Building It Step by Step

This is the intended workshop progression. Each step produces a runnable app.

### Step 1 — Blazor Server shell

Create a Blazor Server project targeting `net10.0` and add `Azure.AI.Projects` (requires `<EnablePreviewFeatures>true</EnablePreviewFeatures>` in the `.csproj`):

```bash
dotnet new blazor -o PizzaBot.ConsumerWeb --interactivity Server --empty
```

Set up `MainLayout.razor` with a minimal header and wire up `IOptions<T>` config sections in `Program.cs`. The app should run and show a blank page at this point.

### Step 2 — Avatar + speech

The server-side avatar plumbing comes from adapting the [Azure Cognitive Services Speech SDK C# web avatar sample](https://github.com/Azure-Samples/cognitive-services-speech-sdk/tree/master/samples/csharp/web/avatar). Add the Speech SDK NuGet package and copy `chat.js` from the sample:

```xml
<PackageReference Include="Microsoft.CognitiveServices.Speech" Version="1.43.0" />
```

Key pieces to build:

- **`AvatarSettings`** — config POCO for speech region, key, avatar character/style
- **`AvatarClientContext`** — per-session state (speech synthesizer, TTS voice, speak queue)
- **`ClientService`** — creates and looks up `AvatarClientContext` instances by `Guid` in a `ConcurrentDictionary`
- **`SpeechTokenService`** — fetches a short-lived speech token (AAD or key-based); refreshed every 9 minutes
- **`IceTokenService`** — fetches a WebRTC ICE relay token from the Speech service; refreshed every minute
- **`TokenRefreshBackgroundService`** — `IHostedService` that runs both token refresh tasks on startup
- **`AvatarController`** — MVC controller with endpoints for `/api/getSpeechToken`, `/api/getIceToken`, `/api/connectAvatar`, `/api/speak`, `/api/stopSpeaking`, `/api/releaseClient`

In `Home.razor`, write an SSR page that:
1. Calls `ClientService.InitializeClient()` to get a `clientId`
2. Renders hidden inputs for `#clientId`, `#talkingAvatarCharacter`, `#talkingAvatarStyle`, `#ttsVoice`, plus checkbox ghost elements that `chat.js` expects
3. Includes a `<script src="js/chat.js"></script>` tag at the bottom

At this point **Start Chat** should display the Lisa avatar and speech recognition should work. The avatar will not respond to messages yet.

### Step 3 — Foundry agent

Add the Azure AI Projects package:

```xml
<PackageReference Include="Azure.AI.Projects" Version="1.2.0-beta.6" />
```

Build **`FoundryAgentService`**: a singleton that wraps `AIProjectClient`, maintains a `ConcurrentDictionary<Guid, SessionState>` of per-client Foundry conversation threads, and exposes `InitializeSession` / `SendMessage` / `RemoveSession`.

Call `agentService.InitializeSession()` from `AvatarController.ConnectAvatar()` (not at page load — the clientId exists but the browser hasn't connected yet). Pass `clientId.ToString("N")` as the `userId` so it matches what the agent will pass to `PlaceOrder`.

Replace the placeholder chat handler in `AvatarController` with:

```csharp
[HttpPost("api/chat")]
public async Task<IActionResult> Chat()
{
    if (!TryGetClientId(out var clientId)) return BadRequest("Invalid ClientId");
    var userQuery = Request.Headers["UserQuery"].FirstOrDefault() ?? string.Empty;
    await HandleUserQuery(userQuery, clientId, Response);
    return new EmptyResult();
}

private async Task HandleUserQuery(string userQuery, Guid clientId, HttpResponse httpResponse)
{
    var agentResponse = await Task.Run(() => agentService.SendMessage(clientId, userQuery));
    // Split into sentences and speak each one via the avatar TTS queue
    foreach (var sentence in SplitIntoSentences(agentResponse))
        await SpeakWithQueue(sentence, 0, clientId, httpResponse);
}
```

Lisa now responds to messages using the Foundry agent, including menu lookups and order placement through the MCP server.

### Step 4 — Orders panel

Add a `PizzaApiService` `HttpClient` wrapper and build `CartPanel.razor` as an `InteractiveServer` component that polls `GET /api/orders` every 5 seconds using a `PeriodicTimer`:

```csharp
_timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
_ = Task.Run(async () =>
{
    while (await _timer.WaitForNextTickAsync(_cts.Token))
    {
        await RefreshOrdersAsync();
        await InvokeAsync(StateHasChanged);
    }
});
```

Add `<CartPanel />` to `Home.razor` and update the CSS grid to show both panels side by side. The orders panel shows all orders by default; a filter input lets users paste a `userId` GUID to narrow to their own orders.

---

## Libraries

| Library | Description | Docs |
|---|---|---|
| `Azure.AI.Projects` | Azure AI Foundry client — creates and manages agents, conversation threads, and tool calls | [Learn](https://learn.microsoft.com/en-us/azure/ai-services/agents/quickstart) |
| `Azure.Identity` | Passwordless authentication via `DefaultAzureCredential` | [Learn](https://learn.microsoft.com/en-us/dotnet/api/overview/azure/identity-readme) |
| `Microsoft.CognitiveServices.Speech` | Azure Speech SDK — server-side TTS for avatar speech synthesis | [Learn](https://learn.microsoft.com/en-us/azure/ai-services/speech-service/speech-sdk) |
| `Newtonsoft.Json` | JSON serialization for avatar API payloads (matches the original SDK sample) | [NuGet](https://www.nuget.org/packages/Newtonsoft.Json) |
| Speech SDK JS (`microsoft.cognitiveservices.speech.sdk.bundle.js`) | Browser-side speech recognition (STT) for microphone input; served locally from `wwwroot/js` | [npm](https://www.npmjs.com/package/microsoft-cognitiveservices-speech-sdk) |

---

## Remarks

### How This Fits With the Other Projects

`PizzaBot.ConsumerWeb` uses the **same `ContosoPizzaBot` agent** as the console projects. The Foundry agent definition (system prompt, file search vector store, MCP server connection, `CalculateNumberOfPizzasToOrder` tool schema) is unchanged. The difference is entirely in how the conversation is driven: instead of a `Console.ReadLine()` loop, input comes from browser STT or a text box, and responses are spoken aloud by Lisa over WebRTC.

The `PizzaCalculator` function tool is still executed client-side (in the ASP.NET Core process), exactly as in `PizzaBot.UseExistingAgent` — the schema lives in Foundry, but the C# implementation must be running here.

### JavaScript Integration

`chat.js` was adapted from the [Azure Cognitive Services Speech SDK C# web avatar sample](https://github.com/Azure-Samples/cognitive-services-speech-sdk/tree/master/samples/csharp/web/avatar). The original sample is an MVC application with a configuration form; this project strips that form away and provides hardcoded values through hidden inputs, keeping `chat.js` close to the original so it remains recognizable against the SDK documentation.

This is a pragmatic integration pattern — the right approach for a workshop where the JS behavior is well understood. A production app might prefer structured JS interop via `IJSRuntime` or a purpose-built Blazor component. See the [Blazor JS interop docs](https://learn.microsoft.com/en-us/aspnet/core/blazor/javascript-interoperability/) for alternatives.

### Speech-to-Text in the Browser

The Speech SDK JS initiates a WebSocket connection directly from the browser to the Azure Speech service. Because of this:

- The SDK cannot use a private endpoint (the connection is made from the user's machine, not your server)
- The speech token is fetched from your server (`/api/getSpeechToken`) and passed to the SDK — the browser never holds the raw API key
- The token is short-lived (10 minutes); the server refreshes it every 9 minutes via `SpeechTokenService`
