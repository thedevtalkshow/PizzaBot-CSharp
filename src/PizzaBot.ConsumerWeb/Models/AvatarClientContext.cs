namespace PizzaBot.ConsumerWeb.Models;

/// <summary>
/// Per-session state for a connected avatar client.
/// One instance is created per browser session (clientId).
/// </summary>
public class AvatarClientContext
{
    public string TtsVoice { get; set; } = "en-US-AvaNeural";
    public string? CustomVoiceEndpointId { get; set; }
    public string? PersonalVoiceSpeakerProfileId { get; set; }

    public object? SpeechSynthesizer { get; set; }
    public object? SpeechSynthesizerConnection { get; set; }
    public bool SpeechSynthesizerConnected { get; set; }

    public bool IsSpeaking { get; set; }
    public string? SpeakingText { get; set; }
    public LinkedList<string> SpokenTextQueue { get; set; } = new();
    public DateTime? LastSpeakTime { get; set; }
}
