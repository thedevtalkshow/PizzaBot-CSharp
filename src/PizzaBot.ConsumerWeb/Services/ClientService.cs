using Microsoft.Extensions.Options;
using PizzaBot.ConsumerWeb.Models;
using System.Collections.Concurrent;

namespace PizzaBot.ConsumerWeb.Services;

/// <summary>
/// Manages per-session <see cref="AvatarClientContext"/> instances.
/// Fixes the original avatar sample bug where a single singleton context was shared across all sessions.
/// </summary>
public class ClientService(IOptions<AvatarSettings> avatarSettings) : IClientService
{
    private readonly AvatarSettings _settings = avatarSettings.Value;
    private readonly ConcurrentDictionary<Guid, AvatarClientContext> _contexts = new();

    public Guid InitializeClient()
    {
        var clientId = Guid.NewGuid();
        _contexts[clientId] = new AvatarClientContext
        {
            TtsVoice = _settings.TtsVoice,
            CustomVoiceEndpointId = null,
            PersonalVoiceSpeakerProfileId = null,
        };
        return clientId;
    }

    public AvatarClientContext GetClientContext(Guid clientId)
    {
        if (!_contexts.TryGetValue(clientId, out var context))
            throw new KeyNotFoundException($"Client context for ID {clientId} was not found.");
        return context;
    }

    public void RemoveClient(Guid clientId) => _contexts.TryRemove(clientId, out _);
}
