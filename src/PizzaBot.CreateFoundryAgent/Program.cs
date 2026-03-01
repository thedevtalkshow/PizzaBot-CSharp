using Azure.AI.Projects;
using Azure.AI.Projects.OpenAI;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using OpenAI.Responses;
using System.Text.Json;

#pragma warning disable OPENAI001

var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables()
    .AddUserSecrets<Program>()
    .Build();

string projectEndpoint = config["PizzaBot:ProjectEndpoint"]
    ?? throw new InvalidOperationException("PizzaBot:ProjectEndpoint is required. Set it in appsettings.json, an environment variable (PizzaBot__ProjectEndpoint), or user secrets.");
string modelDeploymentName = config["PizzaBot:ModelDeploymentName"] ?? "gpt-4o";
string agentName = config["PizzaBot:AgentName"] ?? "PizzabotFromSamples";
string vectorStoreId = config["PizzaBot:VectorStoreId"]
    ?? throw new InvalidOperationException("PizzaBot:VectorStoreId is required. Set it in appsettings.json, an environment variable (PizzaBot__VectorStoreId), or user secrets.");
string mcpServerUri = config["PizzaBot:McpServerUri"]
    ?? throw new InvalidOperationException("PizzaBot:McpServerUri is required. Set it in appsettings.json, an environment variable (PizzaBot__McpServerUri), or user secrets.");

// read instructions from instructions.txt file
string instructions = File.ReadAllText("instructions.txt");

// Connect to your project using the endpoint from your project page
AIProjectClient projectClient = new(endpoint: new Uri(projectEndpoint), tokenProvider: new DefaultAzureCredential());

// Create function tool for pizza calculator
FunctionTool pizzaCalculatorTool = ResponseTool.CreateFunctionTool(
    functionName: "CalculateNumberOfPizzasToOrder",
    functionParameters: BinaryData.FromObjectAsJson(
        new
        {
            Type = "object",
            Properties = new
            {
                numberOfPeople = new
                {
                    Type = "integer",
                    Description = "The number of people we are ordering pizza for."
                },
                appetite = new
                {
                    Type = "string",
                    Description = "The appetite level of the people: 'light' for light eaters (1 slice per person), 'average' for normal appetite (2 slices per person), or 'heavy' for hungry people (4 slices per person).",
                    Enum = new[] { "light", "average", "heavy" }
                }
            },
            Required = new[] { "numberOfPeople" }
        },
        new JsonSerializerOptions() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }
    ),
    strictModeEnabled: false,
    functionDescription: "Calculates the number of pizzas to order based on the number of people and their appetite level."
);

// Create MCP tool for Contoso Pizza ordering
var mcpTool = ResponseTool.CreateMcpTool(
    serverLabel: "contoso-pizza-mcp",
    serverUri: new Uri(mcpServerUri),
    toolCallApprovalPolicy: new McpToolCallApprovalPolicy(GlobalMcpToolCallApprovalPolicy.NeverRequireApproval)
);

// Create your agent with both file search and function tools
PromptAgentDefinition agentDefinition = new(model: modelDeploymentName)
{
    Instructions = instructions,
    Tools =
    {
        new FileSearchTool([vectorStoreId]),
        pizzaCalculatorTool,
        mcpTool
    }
};

// Creates an agent or bumps the existing agent version if parameters have changed
AgentVersion agentVersion = projectClient.Agents.CreateAgentVersion(
    agentName: agentName,
    options: new(agentDefinition));
Console.WriteLine($"Agent created (id: {agentVersion.Id}, name: {agentVersion.Name}, version: {agentVersion.Version})");

ProjectConversation conversation = projectClient.OpenAI.Conversations.CreateProjectConversation();
ProjectResponsesClient responseClient = projectClient.OpenAI.GetProjectResponsesClientForAgent(agentVersion.Name, conversation);

// Conversation loop
string[] exitCommands = ["exit", "quit"];
Console.WriteLine("PizzaBot is ready! Type 'exit' or 'quit' to end the conversation.\n");

while (true)
{
    Console.Write("You: ");
    string? userInput = Console.ReadLine();
    
    if (string.IsNullOrWhiteSpace(userInput))
    {
        continue;
    }

    if (exitCommands.Contains(userInput.Trim().ToLower()))
    {
        Console.WriteLine("Goodbye!");
        break;
    }

    try
    {
        // Create initial request with user message
        CreateResponseOptions responseOptions = new()
        {
            InputItems = { ResponseItem.CreateUserMessageItem(userInput) }
        };

        ResponseResult response;
        bool functionCalled;
        
        // Loop to handle function calls
        do
        {
            response = responseClient.CreateResponse(responseOptions);
            functionCalled = false;

            // Process output items to handle function calls
            foreach (ResponseItem responseItem in response.OutputItems)
            {
                responseOptions.InputItems.Add(responseItem);
                
                if (responseItem is FunctionCallResponseItem functionCall)
                {
                    // Handle the pizza calculator function
                    if (functionCall.FunctionName == "CalculateNumberOfPizzasToOrder")
                    {
                        // Parse arguments
                        using JsonDocument argsDoc = JsonDocument.Parse(functionCall.FunctionArguments);
                        int numberOfPeople = argsDoc.RootElement.GetProperty("numberOfPeople").GetInt32();
                        
                        // Get appetite level if provided, default to "average"
                        string appetite = "average";
                        if (argsDoc.RootElement.TryGetProperty("appetite", out JsonElement appetiteElement))
                        {
                            appetite = appetiteElement.GetString() ?? "average";
                        }
                        
                        // Call the function
                        int pizzaCount = PizzaCalculator.CalculateNumberOfPizzasToOrder(numberOfPeople, appetite);
                        
                        // Add the function output to the conversation
                        responseOptions.InputItems.Add(
                            ResponseItem.CreateFunctionCallOutputItem(
                                functionCall.CallId,
                                pizzaCount.ToString()));
                        
                        functionCalled = true;
                    }
                }
            }
        } while (functionCalled);

        Console.WriteLine($"Assistant: {response.GetOutputText()}\n");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error: {ex.Message}\n");
    }
}