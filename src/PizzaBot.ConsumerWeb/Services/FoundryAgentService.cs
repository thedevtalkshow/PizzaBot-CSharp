#pragma warning disable OPENAI001

using Azure.AI.Projects;
using Azure.AI.Projects.OpenAI;
using Azure.Identity;
using Microsoft.Extensions.Options;
using OpenAI.Responses;
using PizzaBot.ConsumerWeb.Models;
using System.Collections.Concurrent;
using System.Text.Json;

namespace PizzaBot.ConsumerWeb.Services;

/// <summary>
/// Manages per-session conversations with the PizzaBot Foundry agent.
/// Each connected avatar client gets its own conversation thread so history is maintained.
/// </summary>
public class FoundryAgentService
{
    private readonly AIProjectClient _projectClient;
    private readonly string _agentName;
    private readonly ConcurrentDictionary<Guid, SessionState> _sessions = new();

    private record SessionState(
        AgentRecord Agent,
        ProjectConversation Conversation,
        ProjectResponsesClient ResponsesClient);

    public FoundryAgentService(IOptions<PizzaBotSettings> settings)
    {
        var endpoint = settings.Value.ProjectEndpoint;
        _agentName = settings.Value.AgentName;

        if (!string.IsNullOrWhiteSpace(endpoint))
        {
            _projectClient = new AIProjectClient(new Uri(endpoint), new DefaultAzureCredential());
        }
        else
        {
            // Allow startup without config — agent calls will fail gracefully
            _projectClient = null!;
        }
    }

    /// <summary>
    /// Creates a new conversation thread for the given client session.
    /// Sends an initial system context message so the agent knows the user's ordering ID.
    /// </summary>
    public void InitializeSession(Guid clientId, string userId)
    {
        if (_projectClient is null) return;

        AgentRecord agent = _projectClient.Agents.GetAgent(_agentName);
        ProjectConversation conversation = _projectClient.OpenAI.Conversations.CreateProjectConversation();
        ProjectResponsesClient responseClient = _projectClient.OpenAI.GetProjectResponsesClientForAgent(agent, conversation);

        _sessions[clientId] = new SessionState(agent, conversation, responseClient);

        // Inject the user's ordering ID as a context message so the agent uses it when calling the API
        SendContextMessage(clientId, $"[System context: The user's pizza ordering ID is '{userId}'. Always use this exact ID value when calling any tool that requires a userId or customerId.]");
    }

    /// <summary>
    /// Sends a user message and returns the agent's full text response after all tool calls complete.
    /// </summary>
    public string SendMessage(Guid clientId, string userMessage)
    {
        if (_projectClient is null)
            return "I'm not fully configured yet. Please set PizzaBot:ProjectEndpoint in your settings.";

        if (!_sessions.TryGetValue(clientId, out var session))
            return "Session not found. Please refresh and try again.";

        try
        {
            return RunConversationTurn(session.ResponsesClient, userMessage);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Agent] Error: {ex.Message}");
            return "I encountered an error. Please try again.";
        }
    }

    public void RemoveSession(Guid clientId) => _sessions.TryRemove(clientId, out _);

    private void SendContextMessage(Guid clientId, string contextMessage)
    {
        if (!_sessions.TryGetValue(clientId, out var session)) return;
        RunConversationTurn(session.ResponsesClient, contextMessage, suppressOutput: true);
    }

    private static string RunConversationTurn(ProjectResponsesClient responseClient, string userMessage, bool suppressOutput = false)
    {
        var responseOptions = new CreateResponseOptions
        {
            InputItems = { ResponseItem.CreateUserMessageItem(userMessage) }
        };

        ResponseResult response;
        bool functionCalled;

        do
        {
            response = responseClient.CreateResponse(responseOptions);
            functionCalled = false;

            foreach (var item in response.OutputItems)
            {
                responseOptions.InputItems.Add(item);

                if (item is FunctionCallResponseItem fn && fn.FunctionName == "CalculateNumberOfPizzasToOrder")
                {
                    using var argsDoc = JsonDocument.Parse(fn.FunctionArguments);
                    var numberOfPeople = argsDoc.RootElement.GetProperty("numberOfPeople").GetInt32();
                    var appetite = argsDoc.RootElement.TryGetProperty("appetite", out var a)
                        ? a.GetString() ?? "average"
                        : "average";

                    var pizzaCount = PizzaCalculator.Calculate(numberOfPeople, appetite);
                    Console.WriteLine($"[Agent] CalculateNumberOfPizzasToOrder({numberOfPeople}, {appetite}) = {pizzaCount}");

                    responseOptions.InputItems.Add(
                        ResponseItem.CreateFunctionCallOutputItem(fn.CallId, pizzaCount.ToString()));

                    functionCalled = true;
                }
            }
        } while (functionCalled);

        return suppressOutput ? string.Empty : response.GetOutputText();
    }
}
