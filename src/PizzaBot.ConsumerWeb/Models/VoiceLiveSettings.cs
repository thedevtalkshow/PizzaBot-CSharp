namespace PizzaBot.ConsumerWeb.Models;

public class VoiceLiveSettings
{
    /// <summary>
    /// The Voice Live service endpoint, e.g. https://&lt;resource&gt;.cognitiveservices.azure.com/
    /// </summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>
    /// The Azure AI Foundry project name (short name, not the full URL).
    /// Used by AgentSessionConfig to locate the agent within the project.
    /// </summary>
    public string ProjectName { get; set; } = string.Empty;

    public string AvatarCharacter { get; set; } = "lisa";
    public string AvatarStyle { get; set; } = "casual-sitting";
}
