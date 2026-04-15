#pragma warning disable OPENAI001

using Azure.AI.Projects;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using PizzaBot.ConsumerWeb.Models;
using System.Collections.Concurrent;

namespace PizzaBot.ConsumerWeb.Services;

/// <summary>
/// Manages per-session conversations with the PizzaBot Foundry agent.
/// Each connected avatar client gets its own AgentSession so conversation
/// history is maintained per user across turns.
/// </summary>
public class FoundryAgentService
{
    private readonly AIProjectClient? _projectClient;
    private readonly string _agentName;

    // One agent instance is shared; sessions are per-user.
    private ChatClientAgent? _agent;
    private readonly SemaphoreSlim _agentInitLock = new(1, 1);

    private readonly ConcurrentDictionary<Guid, AgentSession> _sessions = new();

    public FoundryAgentService(IOptions<PizzaBotSettings> settings)
    {
        var endpoint = settings.Value.ProjectEndpoint;
        _agentName = settings.Value.AgentName;

        if (!string.IsNullOrWhiteSpace(endpoint))
            _projectClient = new AIProjectClient(new Uri(endpoint), new DefaultAzureCredential());
    }

    /// <summary>
    /// Creates a new AgentSession for the given client and primes it with the
    /// user's ordering ID so the agent always uses the correct userId in tool calls.
    /// </summary>
    public async Task InitializeSessionAsync(Guid clientId, string userId)
    {
        if (_projectClient is null) return;

        var agent = await GetOrCreateAgentAsync();
        var session = await agent.CreateSessionAsync();
        _sessions[clientId] = session;

        // Prime the session with the user's ordering ID so the agent uses it
        // when calling the Pizza API. We discard the response — it's just context.
        await agent.RunAsync(
            $"[System context: The user's pizza ordering ID is '{userId}'. Always use this exact ID value when calling any tool that requires a userId or customerId.]",
            session);
    }

    /// <summary>
    /// Sends a user message and returns the agent's full text response after
    /// all tool calls (e.g. CalculateNumberOfPizzasToOrder) have been dispatched.
    /// </summary>
    public async Task<string> SendMessageAsync(Guid clientId, string userMessage)
    {
        if (_projectClient is null)
            return "I'm not fully configured yet. Please set PizzaBot:ProjectEndpoint in your settings.";

        if (!_sessions.TryGetValue(clientId, out var session))
            return "Session not found. Please refresh and try again.";

        try
        {
            var agent = await GetOrCreateAgentAsync();
            var response = await agent.RunAsync(userMessage, session);
            return response.ToString();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Agent] Error: {ex.Message}");
            return "I encountered an error. Please try again.";
        }
    }

    public void RemoveSession(Guid clientId) => _sessions.TryRemove(clientId, out _);

    // Lazily creates the ChatClientAgent on first use. The agent is shared across
    // all sessions — only the AgentSession differs per user.
    private async Task<ChatClientAgent> GetOrCreateAgentAsync()
    {
        if (_agent is not null) return _agent;

        await _agentInitLock.WaitAsync();
        try
        {
            // Double-check after acquiring the lock
            if (_agent is not null) return _agent;

            // AIFunctionFactory reflects over the method's [Description] attributes to
            // produce the JSON schema the model uses to know when and how to call the tool.
            AITool pizzaCalculatorTool = AIFunctionFactory.Create(PizzaCalculator.CalculateNumberOfPizzasToOrder);

            _agent = await _projectClient!.GetAIAgentAsync(_agentName, tools: [pizzaCalculatorTool]);
            return _agent;
        }
        finally
        {
            _agentInitLock.Release();
        }
    }
}
