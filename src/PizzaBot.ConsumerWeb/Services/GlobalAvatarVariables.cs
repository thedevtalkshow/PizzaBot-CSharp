using PizzaBot.ConsumerWeb.Models;

namespace PizzaBot.ConsumerWeb.Services;

/// <summary>
/// Singleton store for the shared ICE and Speech tokens refreshed in the background.
/// </summary>
public static class GlobalAvatarVariables
{
    public static string? SpeechToken { get; set; }
    public static string? IceToken { get; set; }
}
