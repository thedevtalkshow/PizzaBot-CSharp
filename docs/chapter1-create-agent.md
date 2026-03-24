# Chapter 1 — Creating a Foundry Agent

This chapter walks through building `PizzaBot.CreateFoundryAgent` from scratch. You'll add capability to the agent in four stages — each one runnable — following the same progression as the Python workshop:

1. **Instructions** — a system prompt that shapes the model's personality and behavior
2. **Knowledge** — RAG via a vector store so the agent can answer questions about Contoso Pizza stores
3. **Function tool** — a client-side calculator the model can invoke
4. **MCP tool** — a hosted backend the model calls directly without any client code

The agent **persists in Foundry**. You only need to re-run this project when you want to change the agent's definition. Other projects in this repo connect to the same agent without recreating it.

However, you are free to keep using this version because it will not create a new version of the agent if nothing has changed about its definition. The agent versioning system in Foundry is designed to let you iterate on the agent definition without worrying about creating duplicates.

## What you'll build

- A console app that defines and registers the `ContosoPizzaBot` agent in Azure AI Foundry
- A conversation loop that handles the requires-action cycle when the model calls a client-side function

## Prerequisites

- An Azure AI Foundry project with a `gpt-4o` model deployment
- Azure CLI installed and `az login` completed — this is how `DefaultAzureCredential` authenticates locally

You don't need a vector store yet — you'll create one in Stage 2.

## 1. Create the project

From the `src/` directory:

```bash
dotnet new console -n PizzaBot.CreateFoundryAgent
cd PizzaBot.CreateFoundryAgent
```

Open the generated `.csproj` and add three properties to the `<PropertyGroup>`:

```xml
<EnablePreviewFeatures>true</EnablePreviewFeatures>
<Nullable>enable</Nullable>
<UserSecretsId>50605ced-8c37-4ad2-9585-4059e5963cb7</UserSecretsId>
```

`EnablePreviewFeatures` is required because the Azure AI Projects SDK uses preview OpenAI APIs. `Nullable` keeps the compiler honest about null references. `UserSecretsId` can be any GUID — generate one with `New-Guid` in PowerShell.

Also add an `<ItemGroup>` to copy content files to the output directory:

```xml
<ItemGroup>
  <None Include="appsettings.json" CopyToOutputDirectory="PreserveNewest" />
  <None Include="instructions.txt" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

## 2. Add NuGet packages

```bash
dotnet add package Azure.AI.Projects --version "1.2.*-*" --prerelease
dotnet add package Microsoft.Extensions.Configuration.Json
dotnet add package Microsoft.Extensions.Configuration.EnvironmentVariables
dotnet add package Microsoft.Extensions.Configuration.UserSecrets
```

`Azure.AI.Projects` is the Azure AI Foundry SDK — it covers agents, conversations, and responses. The three `Microsoft.Extensions.Configuration.*` packages provide the layered config system used throughout .NET.

## 3. Configuration

Create `appsettings.json` in the project root with the basic settings to get started:

```json
{
  "PizzaBot": {
    "ProjectEndpoint": "",
    "ModelDeploymentName": "gpt-4o",
    "AgentName": "ContosoPizzaBot"
  }
}
```

`ProjectEndpoint` contains a personal Azure resource identifier and should never be committed. Set it in user secrets:

```bash
dotnet user-secrets set "PizzaBot:ProjectEndpoint" "https://<resource>.services.ai.azure.com/api/projects/<project>"
```

You'll add `VectorStoreId` and `McpServerUri` to this file as you reach those stages.

Now wire up configuration in `Program.cs`. The priority order matters: **user secrets win over environment variables, which win over `appsettings.json`**. This lets you override any value at runtime without touching files.

```csharp
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
    ?? throw new InvalidOperationException("PizzaBot:ProjectEndpoint is required. Set it in user secrets.");
string modelDeploymentName = config["PizzaBot:ModelDeploymentName"] ?? "gpt-4o";
string agentName = config["PizzaBot:AgentName"] ?? "ContosoPizzaBot";
```

The `?? throw new InvalidOperationException(...)` pattern fails fast at startup with a clear message rather than a cryptic null-reference somewhere deep in the SDK.

## 4. Connect to Azure AI Foundry

```csharp
AIProjectClient projectClient = new(
    endpoint: new Uri(projectEndpoint),
    tokenProvider: new DefaultAzureCredential());
```

`DefaultAzureCredential` tries a chain of credential sources in order — the Azure CLI token from `az login` locally, managed identity in Azure. You write the same line in both environments; the credential resolves differently based on context.

---

## Stage 1 — Instructions

### 5. Load the system prompt from a file

Create `instructions.txt` in the project root:

```
You are Contoso PizzaBot, an AI assistant that helps users order pizza.

Your primary role is to assist users in ordering pizza, checking menus, and tracking order status.

## guidelines
When interacting with users, follow these guidelines:
1. Be friendly, helpful, and concise in your responses.
1. When users want to order pizza, make sure to gather all necessary information (pizza type, options).
1. Contoso Pizza has stores in multiple locations. Before making an order, check to see if the user has specified the store to order from. 
   If they have not, assume they are ordering from the San Francisco, USA store.
1. Your tools will provide prices in USD. 
   When providing prices to the user, convert to the currency appropriate to the store the user is ordering from.
1. Your tools will provide pickup times in UTC. 
   When providing pickup times to the user, convert to the time zone appropriate to the store the user is ordering from.
1. When users ask about the menu, provide the available options clearly. List at most 5 menu entries at a time, and ask the user if they'd like to hear more.
1. If users ask about order status, help them check using their order ID.
1. If you're uncertain about any information, ask clarifying questions.
1. Always confirm orders before placing them to ensure accuracy.
1. Do not talk about anything else then Pizza
1. If you do not have a UserId and Name, always start with requesting that.

## Response
You will interact with users primarily through voice, so your responses should be natural, short and conversational. 
1. **Only use plain text**
2. No emoticons, No markup, No markdown, No html, only plain text.
3. Use short and conversational language.

When customers ask about how much pizza they need for a group, use the pizza calculator function to provide helpful recommendations based on the number of people and their appetite level.
```

Then read it in `Program.cs`:

```csharp
// Externalizing the prompt means you can tune it without recompiling.
// Prompt engineering is iterative — you don't want a recompile on every tweak.
string instructions = File.ReadAllText("instructions.txt");
```

### 6. Create the agent

```csharp
PromptAgentDefinition agentDefinition = new(model: modelDeploymentName)
{
    Instructions = instructions
};

// Creates the agent on first run; bumps the version on subsequent runs if the definition changed.
AgentVersion agentVersion = projectClient.Agents.CreateAgentVersion(
    agentName: agentName,
    options: new(agentDefinition));

Console.WriteLine($"Agent created (id: {agentVersion.Id}, name: {agentVersion.Name}, version: {agentVersion.Version})");
```

`CreateAgentVersion` is idempotent by name. The first call creates the agent in Foundry; subsequent calls create a new version if anything changed. The agent lives in your Foundry project — you can view it in the portal.

### 7. Start a conversation

```csharp
// A conversation is a server-side thread. Foundry keeps the message history.
ProjectConversation conversation = projectClient.OpenAI.Conversations.CreateProjectConversation();
ProjectResponsesClient responseClient = projectClient.OpenAI.GetProjectResponsesClientForAgent(agentVersion.Name, conversation);
```

Each `ProjectConversation` is a distinct thread. The model sees the full history of every message in that thread, which is how it maintains context across turns.

### 8. The conversation loop

```csharp
string[] exitCommands = ["exit", "quit"];
Console.WriteLine("PizzaBot is ready! Type 'exit' or 'quit' to end the conversation.\n");

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

    try
    {
        ResponseResult response = responseClient.CreateResponse(new CreateResponseOptions
        {
            InputItems = { ResponseItem.CreateUserMessageItem(userInput) }
        });

        Console.WriteLine($"Assistant: {response.GetOutputText()}\n");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error: {ex.Message}\n");
    }
}
```

### ▶ Run it

```bash
dotnet run
```

The agent will ask for your name and user ID (per the system prompt). Try asking about pizza — it only knows what the model was trained on at this point, not Contoso Pizza specifically.

---

## Stage 2 — Knowledge

RAG (Retrieval-Augmented Generation) via a vector store lets the agent answer questions from your documents rather than making things up from training data. You'll upload the Contoso Pizza store knowledge files and attach the resulting vector store to the agent.

### 9. Create a vector store

The `scripts/KnowledgeUpload.cs` script uploads the knowledge documents and creates a vector store in your Foundry project. It uses the `dotnet run` scripting feature introduced in .NET 10 — no project file required, NuGet packages are declared inline with `#:package` directives.

From the repo root:

```bash
dotnet run scripts/KnowledgeUpload.cs https://<resource>.services.ai.azure.com/api/projects/<project>
```

The script will print a vector store ID at the end:

```
Created vector store with ID: vs_abc123...
Set this as PizzaBot:VectorStoreId in your user secrets.
```

Copy that ID and store it in user secrets:

```bash
dotnet user-secrets set "PizzaBot:VectorStoreId" "vs_abc123..."
```

Also add `VectorStoreId` to `appsettings.json` as an empty placeholder so others know the setting exists:

```json
{
  "PizzaBot": {
    "ProjectEndpoint": "",
    "ModelDeploymentName": "gpt-4o",
    "AgentName": "ContosoPizzaBot",
    "VectorStoreId": ""
  }
}
```

### 10. Read the vector store ID

Add to the config section in `Program.cs`:

```csharp
string vectorStoreId = config["PizzaBot:VectorStoreId"]
    ?? throw new InvalidOperationException("PizzaBot:VectorStoreId is required. Run scripts/KnowledgeUpload.cs to create one, then set it in user secrets.");
```

### 11. Add FileSearchTool to the agent

Update the `agentDefinition` to include `FileSearchTool`:

```csharp
PromptAgentDefinition agentDefinition = new(model: modelDeploymentName)
{
    Instructions = instructions,
    Tools =
    {
        // RAG — the model searches these documents before answering store questions
        new FileSearchTool([vectorStoreId])
    }
};
```

Also update `instructions.txt` to tell the model it has a knowledge source available. Add this section:

```
## Tools & Data Access
- Use the **Contoso Pizza Store Information Vector Store** to search get information about stores, like address and opening times.
    - **Tool:** `file_search`
    - Only return information found in the vector store or uploaded files.
    - If the information is ambiguous or not found, ask the user for clarification.
```

### ▶ Run it

```bash
dotnet run
```

Ask about a Contoso Pizza store location or hours. The agent now searches the uploaded documents before answering — you'll get accurate Contoso-specific responses instead of model hallucinations.

---

## Stage 3 — Function Tool

A function tool is a capability that **executes on the client**. Foundry tells the model the tool exists and what arguments it takes; when the model decides to use it, it sends a `FunctionCallResponseItem` back to your code. Your code runs the function and posts the result back. Foundry never executes function code.

### 12. Add PizzaCalculator.cs

Add `PizzaCalculator.cs` to the project:

```csharp
using System.ComponentModel;

public class PizzaCalculator
{
    [Description("Calculates the number of pizzas to order based on the number of people and their appetite level.")]
    public static int CalculateNumberOfPizzasToOrder(
        [Description("The number of people we are ordering pizza for.")] int numberOfPeople,
        [Description("The appetite level: 'light' (1 slice per person), 'average' (2 slices per person), or 'heavy' (4 slices per person). Defaults to 'average'.")] string appetite = "average")
    {
        int slicesPerPerson = appetite.ToLower() switch
        {
            "light" => 1,
            "heavy" => 4,
            _ => 2
        };

        Console.WriteLine($"PizzaCalculator: {numberOfPeople} people, {appetite} appetite → {slicesPerPerson} slices/person");

        int totalSlicesNeeded = numberOfPeople * slicesPerPerson;
        return (int)Math.Ceiling((double)totalSlicesNeeded / 8);
    }
}
```

### 13. Register the FunctionTool

Add to `Program.cs` before the agent definition:

```csharp
// The schema lives in Foundry so the model knows what arguments to pass.
// Execution always happens here on the client — Foundry never runs this code.
FunctionTool pizzaCalculatorTool = ResponseTool.CreateFunctionTool(
    functionName: "CalculateNumberOfPizzasToOrder",
    functionDescription: "Calculates the number of pizzas to order based on the number of people and their appetite level.",
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
                    Description = "The appetite level: 'light' (1 slice per person), 'average' (2 slices per person), or 'heavy' (4 slices per person).",
                    Enum = new[] { "light", "average", "heavy" }
                }
            },
            Required = new[] { "numberOfPeople" }
        },
        new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }
    ),
    strictModeEnabled: false
);
```

`BinaryData.FromObjectAsJson` serializes the anonymous object to JSON inline — there's no cleaner way to express a JSON Schema in C# without a dedicated library. The `CamelCase` policy is required because the OpenAI schema spec expects camelCase property names.

### 14. Add the tool to the agent definition

```csharp
PromptAgentDefinition agentDefinition = new(model: modelDeploymentName)
{
    Instructions = instructions,
    Tools =
    {
        new FileSearchTool([vectorStoreId]),
        pizzaCalculatorTool
    }
};
```

### 15. Update the conversation loop

The simple `CreateResponse` call no longer covers all cases. When the model calls `CalculateNumberOfPizzasToOrder`, it returns a `FunctionCallResponseItem` instead of text — you execute the function, post the result back, and call `CreateResponse` again. Replace the loop body:

```csharp
try
{
    CreateResponseOptions responseOptions = new()
    {
        InputItems = { ResponseItem.CreateUserMessageItem(userInput) }
    };

    ResponseResult response;
    bool functionCalled;

    // The model may call a function, get the result, then call another, then respond.
    // Loop until it stops requesting function calls.
    do
    {
        response = responseClient.CreateResponse(responseOptions);
        functionCalled = false;

        foreach (ResponseItem responseItem in response.OutputItems)
        {
            // Every output item feeds back into the next request so the model sees full history.
            responseOptions.InputItems.Add(responseItem);

            if (responseItem is FunctionCallResponseItem functionCall)
            {
                if (functionCall.FunctionName == "CalculateNumberOfPizzasToOrder")
                {
                    using JsonDocument argsDoc = JsonDocument.Parse(functionCall.FunctionArguments);
                    int numberOfPeople = argsDoc.RootElement.GetProperty("numberOfPeople").GetInt32();

                    string appetite = "average";
                    if (argsDoc.RootElement.TryGetProperty("appetite", out JsonElement appetiteElement))
                        appetite = appetiteElement.GetString() ?? "average";

                    int pizzaCount = PizzaCalculator.CalculateNumberOfPizzasToOrder(numberOfPeople, appetite);

                    responseOptions.InputItems.Add(
                        ResponseItem.CreateFunctionCallOutputItem(functionCall.CallId, pizzaCount.ToString()));

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
```

### ▶ Run it

```bash
dotnet run
```

Ask "how many pizzas do I need for 12 hungry people?" — you'll see `PizzaCalculator` print its output to the console before the model responds. That's the requires-action cycle made visible. Chapter 3 shows how the Agent Framework eliminates this loop entirely.

---

## Stage 4 — MCP Tool

An MCP tool is the opposite of a function tool: it executes **on the server**. When the model decides to call an MCP tool, Foundry contacts the MCP server directly — your client never sees the call happen, and you write no dispatch code.

### 16. Add McpServerUri to configuration

Add to `appsettings.json`:

```json
{
  "PizzaBot": {
    "ProjectEndpoint": "",
    "ModelDeploymentName": "gpt-4o",
    "AgentName": "ContosoPizzaBot",
    "VectorStoreId": "",
    "McpServerUri": "https://ca-pizza-mcp-sc6u2typoxngc.graypond-9d6dd29c.eastus2.azurecontainerapps.io/mcp"
  }
}
```

`McpServerUri` is pre-filled with the hosted workshop backend so you can run without standing up a local server. To use your own local MCP server instead, override it in user secrets (see [Running the Full Stack Locally](local-stack.md)).

Add to the config section in `Program.cs`:

```csharp
string mcpServerUri = config["PizzaBot:McpServerUri"]
    ?? throw new InvalidOperationException("PizzaBot:McpServerUri is required. Set it in appsettings.json or user secrets.");
```

### 17. Define the MCP tool

Add to `Program.cs` before the agent definition:

```csharp
// A hosted tool — Foundry calls the MCP server directly, no client dispatch code needed.
var mcpTool = ResponseTool.CreateMcpTool(
    serverLabel: "contoso-pizza-mcp",
    serverUri: new Uri(mcpServerUri),
    toolCallApprovalPolicy: new McpToolCallApprovalPolicy(GlobalMcpToolCallApprovalPolicy.NeverRequireApproval)
);
```

`NeverRequireApproval` tells Foundry it can invoke any MCP tool without pausing to ask the client for confirmation first.

### 18. Add the tool to the agent definition

```csharp
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
```

The conversation loop is unchanged — MCP tools are server-side and require no requires-action handling.

### ▶ Run it

```bash
dotnet run
```

The agent now has full capabilities: it knows Contoso store information from the knowledge base, can calculate pizza quantities via the local function, and can browse the menu and place orders through the MCP server.

---

## 19. Add to the solution

```bash
dotnet sln ../PizzaOrdering.slnx add PizzaBot.CreateFoundryAgent.csproj
```
