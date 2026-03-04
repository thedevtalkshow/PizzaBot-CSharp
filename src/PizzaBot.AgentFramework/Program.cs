using Azure.AI.Projects;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;

// get our values from configuration (appsettings.json, environment variables, or user secrets)
var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables()
    .AddUserSecrets<Program>()
    .Build();

string projectEndpoint = config["PizzaBot:ProjectEndpoint"]
    ?? throw new InvalidOperationException("PizzaBot:ProjectEndpoint is required. Set it in appsettings.json, an environment variable (PizzaBot__ProjectEndpoint), or user secrets.");
string modelDeploymentName = config["PizzaBot:ModelDeploymentName"] ?? "gpt-4o";
string agentName = config["PizzaBot:AgentName"] ?? "ContosoPizzaBotWithAgentFramework";
string vectorStoreId = config["PizzaBot:VectorStoreId"]
    ?? throw new InvalidOperationException("PizzaBot:VectorStoreId is required. Set it in appsettings.json, an environment variable (PizzaBot__VectorStoreId), or user secrets.");
string mcpServerUri = config["PizzaBot:McpServerUri"]
    ?? throw new InvalidOperationException("PizzaBot:McpServerUri is required. Set it in appsettings.json, an environment variable (PizzaBot__McpServerUri), or user secrets.");

// set the Pizzabot's instructions
string instructions = File.ReadAllText("instructions.txt");

// Connect to the Foundry Persistent Agents service
AIProjectClient aiProjectClient = new(new Uri(projectEndpoint), new DefaultAzureCredential());

// Define the function tool.
AITool tool = AIFunctionFactory.Create(PizzaCalculator.CalculateNumberOfPizzasToOrder);

// Get an existing agent from Foundry, and define the function tool.
ChatClientAgent agent = await aiProjectClient.GetAIAgentAsync(
    agentName,
    tools: [tool]);

// Create a session to maintain conversation history server-side
AgentSession session = await agent.CreateSessionAsync();

// Conversation loop
string[] exitCommands = ["exit", "quit"];
Console.WriteLine("ContosoPizzaBot (Agent Framework) is ready! Type 'exit' or 'quit' to end.\n");

while (true)
{
    Console.Write("You: ");
    string? userInput = Console.ReadLine();

    if (string.IsNullOrWhiteSpace(userInput)) continue;

    if (exitCommands.Contains(userInput.Trim().ToLower()))
    {
        Console.WriteLine("Goodbye!");
        break;
    }

    AgentResponse response = await agent.RunAsync(userInput, session);
    Console.WriteLine($"Agent: {response}\n");
}