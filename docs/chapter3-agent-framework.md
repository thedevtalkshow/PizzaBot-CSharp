# Chapter 3 — Using the Agent Framework

In Chapters 1 and 2 you handled the conversation loop manually: after each model response, you iterated over `OutputItems`, detected `FunctionCallResponseItem`, executed the function yourself, and posted the result back with `CreateFunctionCallOutputItem`. The requires-action cycle was fully visible — which was the point.

The **Microsoft Agents SDK** eliminates all of that. The loop collapses to a single awaited call and the framework handles dispatch. This chapter builds `PizzaBot.AgentFramework` — the same conversation, zero boilerplate.

## What you'll build

- A console app that connects to the same Foundry agent created in Chapter 1
- A conversation loop driven by `agent.RunAsync(userInput, session)`
- Automatic `PizzaCalculator` tool dispatch via `AIFunctionFactory`

No manual function-call handling. No `FunctionCallResponseItem`. No `do/while`.

## 1. Create the project

```bash
dotnet new console -n PizzaBot.AgentFramework
cd PizzaBot.AgentFramework
```

Add packages:

```bash
dotnet add package Azure.Identity
dotnet add package Azure.AI.Projects --version "1.2.*-*" --prerelease
dotnet add package Microsoft.Agents.AI.AzureAI --version "1.0.0-rc2"
dotnet add package Microsoft.Extensions.Configuration.Json
dotnet add package Microsoft.Extensions.Configuration.EnvironmentVariables
dotnet add package Microsoft.Extensions.Configuration.UserSecrets
```

`Microsoft.Agents.AI.AzureAI` transitively brings in `Microsoft.Agents.AI` and `Microsoft.Extensions.AI` — you don't need to reference those separately.

Enable user secrets (needed to store your Azure endpoint securely):

```bash
dotnet user-secrets init
```

## 2. Configuration

Create `appsettings.json`. The hosted workshop MCP server URI is pre-filled so you can run immediately without standing up a local stack:

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

Set the secrets that shouldn't be committed:

```bash
dotnet user-secrets set "PizzaBot:ProjectEndpoint" "https://<your-project>.api.azureml.ms"
dotnet user-secrets set "PizzaBot:VectorStoreId" "vs_..."
```

Mark `appsettings.json` to copy to output in the `.csproj`:

```xml
<ItemGroup>
  <None Include="appsettings.json" CopyToOutputDirectory="PreserveNewest" />
  <None Include="instructions.txt" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

The same five settings as Chapter 1 — `ProjectEndpoint`, `ModelDeploymentName`, `AgentName`, `VectorStoreId`, `McpServerUri` — in the same layered order: user secrets win over environment variables, which win over `appsettings.json`.

| Setting | Where to set it |
|---|---|
| `ProjectEndpoint` | User secrets |
| `ModelDeploymentName` | `appsettings.json` |
| `AgentName` | `appsettings.json` |
| `VectorStoreId` | User secrets |
| `McpServerUri` | `appsettings.json` (or user secrets for a local tunnel) |

## 3. PizzaCalculator.cs

Copy `PizzaCalculator.cs` from `PizzaBot.CreateFoundryAgent` unchanged. The implementation is identical — the difference is in how you register it.

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

        Console.WriteLine($"PizzaCalculator: Calculating pizzas for {numberOfPeople} people with {appetite} appetite ({slicesPerPerson} slices/person)...");

        int slicesPerPizza = 12;
        int totalSlicesNeeded = numberOfPeople * slicesPerPerson;
        return (int)Math.Ceiling((double)totalSlicesNeeded / slicesPerPizza);
    }
}
```

In Chapter 1 you had to hand-write the JSON schema — the `Properties`, `Type`, `Description`, `Required` object — and pass it to `ResponseTool.CreateFunctionTool`. Here, `AIFunctionFactory` reads the `[Description]` attributes and generates the schema automatically. One line replaces ~20.

## 4. Program.cs

Notice there is no `#pragma warning disable OPENAI001` at the top. The Agent Framework doesn't use the preview OpenAI APIs directly, so no suppression is needed.

```csharp
using Azure.AI.Projects;
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
string agentName = config["PizzaBot:AgentName"] ?? "ContosoPizzaBot";
string vectorStoreId = config["PizzaBot:VectorStoreId"]
    ?? throw new InvalidOperationException("PizzaBot:VectorStoreId is required. Set it in appsettings.json, an environment variable (PizzaBot__VectorStoreId), or user secrets.");
string mcpServerUri = config["PizzaBot:McpServerUri"]
    ?? throw new InvalidOperationException("PizzaBot:McpServerUri is required. Set it in appsettings.json, an environment variable (PizzaBot__McpServerUri), or user secrets.");

string instructions = File.ReadAllText("instructions.txt");

// Connect to the Foundry Persistent Agents service
AIProjectClient aiProjectClient = new(new Uri(projectEndpoint), new DefaultAzureCredential());
```

### Register the tool

```csharp
// AIFunctionFactory reads [Description] attributes and builds the JSON schema automatically.
// In Chapter 1 this was ~20 lines of hand-written schema; here it's one.
AITool tool = AIFunctionFactory.Create(PizzaCalculator.CalculateNumberOfPizzasToOrder);
```

`AIFunctionFactory` is from `Microsoft.Extensions.AI` — the abstraction layer that lets AI tooling work across different model providers. It introspects the method's `[Description]` attributes and produces a strongly-typed tool descriptor the framework can route calls to.

### Get the agent and create a session

```csharp
// Fetch the agent record from Foundry and wire up the local tools for dispatch
ChatClientAgent agent = await aiProjectClient.GetAIAgentAsync(
    agentName,
    tools: [tool]);

// Create a session — equivalent to a conversation thread, history is maintained server-side
AgentSession session = await agent.CreateSessionAsync();
```

`GetAIAgentAsync` does two things: fetches the existing agent definition from Foundry (the system prompt, file search index, and MCP configuration you set up in Chapter 1) and registers the local tools so the framework knows how to execute them when the model requests it.

`CreateSessionAsync` creates a server-side thread. Every call to `RunAsync` on the same session continues the same conversation.

### The loop

```csharp
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
```

Compare this to Chapter 2's loop: the entire `do/while`, the `foreach (ResponseItem responseItem in response.OutputItems)`, the `FunctionCallResponseItem` check, the `JsonDocument.Parse`, the `CreateFunctionCallOutputItem` — all gone. `RunAsync` handles the complete requires-action cycle internally. If the model requests `CalculateNumberOfPizzasToOrder`, the framework calls it, posts the result, and continues the response — transparently.

## 5. Run it

```bash
dotnet run
```

You should see:

```
ContosoPizzaBot (Agent Framework) is ready! Type 'exit' or 'quit' to end.

You: What pizzas do you have?
Agent: We have 16 pizzas on the menu! Here are some highlights...
```

Try asking how many pizzas to order for a group — the `PizzaCalculator` tool will fire silently and the agent will incorporate the result into its answer.

## 6. Add to the solution

```bash
dotnet sln ../PizzaOrdering.slnx add PizzaBot.AgentFramework/PizzaBot.AgentFramework.csproj
```

## Libraries used

| Package | Purpose | Docs |
|---|---|---|
| `Azure.AI.Projects` | Foundry Persistent Agents client — `AIProjectClient`, `GetAIAgentAsync` | [docs.microsoft.com](https://learn.microsoft.com/azure/ai-foundry/) |
| `Microsoft.Agents.AI.AzureAI` | Agent Framework for Azure AI — `ChatClientAgent`, `AgentSession`, `AgentResponse` | [github.com/microsoft/agents](https://github.com/microsoft/agents) |
| `Microsoft.Extensions.AI` | Cross-provider AI abstractions — `AIFunctionFactory`, `AITool` | [learn.microsoft.com](https://learn.microsoft.com/dotnet/ai/microsoft-extensions-ai) |
| `Azure.Identity` | `DefaultAzureCredential` for passwordless auth | [learn.microsoft.com](https://learn.microsoft.com/dotnet/azure/sdk/authentication) |
