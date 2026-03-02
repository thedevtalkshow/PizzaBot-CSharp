using Azure.AI.Agents.Persistent;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;

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

string instructions = File.ReadAllText("instructions.txt");

// Connect to the Foundry Persistent Agents service
PersistentAgentsClient client = new PersistentAgentsClient(projectEndpoint, new DefaultAzureCredential());

// --- Tool definitions registered with the agent in Foundry ---

// Function tool: schema is declared here so the model knows about the tool.
// The implementation runs locally and is dispatched automatically by the Agent Framework.
FunctionToolDefinition pizzaCalculatorToolDef = new(
    name: "CalculateNumberOfPizzasToOrder",
    description: "Calculates the number of pizzas to order based on the number of people and their appetite level.",
    parameters: BinaryData.FromObjectAsJson(new
    {
        type = "object",
        properties = new
        {
            numberOfPeople = new
            {
                type = "integer",
                description = "The number of people we are ordering pizza for."
            },
            appetite = new
            {
                type = "string",
                description = "The appetite level: 'light' (1 slice per person), 'average' (2 slices per person), or 'heavy' (4 slices per person). Defaults to 'average'.",
                @enum = new[] { "light", "average", "heavy" }
            }
        },
        required = new[] { "numberOfPeople" }
    }));

// File search tool: queries the Contoso pizza menu vector store hosted in Foundry
FileSearchToolDefinition fileSearchToolDef = new();
ToolResources toolResources = new()
{
    FileSearch = new FileSearchToolResource()
    {
        VectorStoreIds = { vectorStoreId }
    }
};

// MCP tool: calls the Contoso order-management MCP server hosted in Azure
const string mcpServerLabel = "contoso-pizza-mcp";
MCPToolDefinition mcpToolDef = new(serverLabel: mcpServerLabel, serverUrl: mcpServerUri);

// --- Find existing agent or create a new one ---
PersistentAgent? existingAgent = null;
await foreach (PersistentAgent a in client.Administration.GetAgentsAsync())
{
    if (a.Name == agentName)
    {
        existingAgent = a;
        break;
    }
}

PersistentAgent agentDefinition;
if (existingAgent is not null)
{
    agentDefinition = existingAgent;
    Console.WriteLine($"Using existing agent: {agentDefinition.Name} (id: {agentDefinition.Id})");
}
else
{
    agentDefinition = (await client.Administration.CreateAgentAsync(
        model: modelDeploymentName,
        name: agentName,
        instructions: instructions,
        tools: [pizzaCalculatorToolDef, fileSearchToolDef, mcpToolDef],
        toolResources: toolResources)).Value;
    Console.WriteLine($"Agent created: {agentDefinition.Name} (id: {agentDefinition.Id})");
}

// Retrieve as an AIAgent (Agent Framework type)
AIAgent agent = await client.GetAIAgentAsync(agentDefinition.Id);

// Create a session to maintain conversation history server-side
AgentSession session = await agent.CreateSessionAsync();

// Run options:
//   - Tools: provides the local implementation so the Agent Framework can dispatch
//     function calls automatically (no manual requires_action loop needed)
//   - RawRepresentationFactory: configures the underlying run to allow MCP tool
//     calls without per-call approval prompts
AIFunction pizzaCalcFunction = AIFunctionFactory.Create(PizzaCalculator.CalculateNumberOfPizzasToOrder);
ChatClientAgentRunOptions runOptions = new()
{
    ChatOptions = new()
    {
        Tools = [pizzaCalcFunction],
        RawRepresentationFactory = (_) => new ThreadAndRunOptions()
        {
            ToolResources = new MCPToolResource(serverLabel: mcpServerLabel)
            {
                RequireApproval = new MCPApproval("never"),
            }.ToToolResources()
        }
    }
};

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

    AgentResponse response = await agent.RunAsync(userInput, session, runOptions);
    Console.WriteLine($"Agent: {response}\n");
}
