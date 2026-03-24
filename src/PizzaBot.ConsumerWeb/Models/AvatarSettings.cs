namespace PizzaBot.ConsumerWeb.Models;

public class AvatarSettings
{
    public static readonly List<string> SentenceLevelPunctuations = [".", "?", "!", ":", ";", "。", "？", "！", "：", "；"];
    public static readonly bool RepeatSpeakingSentenceAfterReconnection = true;
    public static readonly bool EnableQuickReply = false;
    public static readonly bool EnableDisplayTextAlignmentWithSpeech = false;
    public static readonly bool EnableAudioAudit = false;

    public string? SpeechRegion { get; set; }
    public string? SpeechKey { get; set; }
    public string? SpeechPrivateEndpoint { get; set; }
    public string? SpeechResourceUrl { get; set; }
    public string? UserAssignedManagedIdentityClientId { get; set; }
    public string? IceServerUrl { get; set; }
    public string? IceServerUrlRemote { get; set; }
    public string? IceServerUsername { get; set; }
    public string? IceServerPassword { get; set; }

    // Avatar UI defaults
    public string TtsVoice { get; set; } = "en-US-AvaNeural";
    public string AvatarCharacter { get; set; } = "lisa";
    public string AvatarStyle { get; set; } = "casual-sitting";
    public string BackgroundColor { get; set; } = "#1a1a2e";
}
