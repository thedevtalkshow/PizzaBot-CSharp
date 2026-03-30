using Azure.AI.VoiceLive;
using Azure.Identity;
using Microsoft.Extensions.Options;
using PizzaBot.ConsumerWeb.Models;

namespace PizzaBot.ConsumerWeb.Services;

/// <summary>
/// Factory for creating per-connection Voice Live sessions.
/// Holds a single VoiceLiveClient (Entra ID auth) and vends sessions on demand.
/// </summary>
public class VoiceLiveSessionService
{
    private readonly VoiceLiveClient _client;
    private readonly VoiceLiveSettings _settings;
    private readonly string _agentName;

    public VoiceLiveSessionService(IOptions<VoiceLiveSettings> voiceLiveSettings, IOptions<PizzaBotSettings> pizzaBotSettings)
    {
        _settings = voiceLiveSettings.Value;
        _agentName = pizzaBotSettings.Value.AgentName;

        var endpoint = _settings.Endpoint;
        if (string.IsNullOrWhiteSpace(endpoint))
            throw new InvalidOperationException(
                "VoiceLive:Endpoint is required. Set it in appsettings.json, environment variables, or user secrets.");

        _client = new VoiceLiveClient(new Uri(endpoint), new DefaultAzureCredential());
    }

    /// <summary>
    /// Creates and connects a new Voice Live session targeting the Foundry agent, with avatar streaming enabled.
    /// </summary>
    public async Task<VoiceLiveSession> CreateSessionAsync(CancellationToken cancellationToken = default)
    {
        var projectName = _settings.ProjectName;
        if (string.IsNullOrWhiteSpace(projectName))
            throw new InvalidOperationException(
                "VoiceLive:ProjectName is required. Set it in appsettings.json, environment variables, or user secrets.");

        var agentConfig = new AgentSessionConfig(_agentName, projectName);
        var target = SessionTarget.FromAgent(agentConfig);

        var options = new VoiceLiveSessionOptions
        {
            // Avatar streams lip-synced video + audio via WebRTC back to the browser
            Avatar = new AvatarConfiguration(_settings.AvatarCharacter, customized: false)
            {
                Style = _settings.AvatarStyle,
                OutputProtocol = AvatarOutputProtocol.Webrtc,
            },
            // Enable transcription so we can surface what the user said in the chat history
            InputAudioTranscription = new AudioInputTranscriptionOptions(AudioInputTranscriptionOptionsModel.AzureSpeech),
            // Without echo cancellation the avatar's own speaker output loops back into the mic,
            // which confuses the VAD and causes missed or dropped turns.
            InputAudioEchoCancellation = new AudioEchoCancellation(),
            // Reduce ambient noise so the EOU detector isn't fooled by background sounds.
            InputAudioNoiseReduction = new AudioNoiseReduction(AudioNoiseReductionType.AzureDeepNoiseSuppression),
            // Low silence duration fires end-of-turn sooner after the user stops speaking.
            // The default can feel unresponsive — 500ms trades a small accuracy hit for
            // noticeably better perceived responsiveness.
            TurnDetection = new AzureSemanticVadTurnDetectionEn
            {
                SilenceDuration = TimeSpan.FromMilliseconds(500),
            },
        };

        return await _client.StartSessionAsync(target, options, cancellationToken);
    }
}

