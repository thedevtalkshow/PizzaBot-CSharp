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
string agentName = config["PizzaBot:AgentName"] ?? "PizzabotFromSamples";

// Connect to Foundry and get the existing agent
AIProjectClient projectClient = new(endpoint: new Uri(projectEndpoint), tokenProvider: new DefaultAzureCredential());

AgentRecord agentRecord = projectClient.Agents.GetAgent(agentName);
Console.WriteLine($"Using existing agent: {agentRecord.Name} (id: {agentRecord.Id})\n");

// Create a conversation thread to maintain context
ProjectConversation conversation = projectClient.OpenAI.Conversations.CreateProjectConversation();
ProjectResponsesClient responseClient = projectClient.OpenAI.GetProjectResponsesClientForAgent(agentRecord, conversation);

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
        // The agent definition in Foundry has the tools, but execution happens here
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
                    // The agent tells us which function to call - we execute it
                    if (functionCall.FunctionName == "CalculateNumberOfPizzasToOrder")
                    {
                        // Parse arguments
                        using JsonDocument argsDoc = JsonDocument.Parse(functionCall.FunctionArguments);
                        int numberOfPeople = argsDoc.RootElement.GetProperty("numberOfPeople").GetInt32();
                        
                        // Get appetite level if provided
                        string appetite = "average";
                        if (argsDoc.RootElement.TryGetProperty("appetite", out JsonElement appetiteElement))
                        {
                            appetite = appetiteElement.GetString() ?? "average";
                        }
                        
                        // Execute the actual function
                        int pizzaCount = PizzaCalculator.CalculateNumberOfPizzasToOrder(numberOfPeople, appetite);
                        
                        // Return the result to the agent
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