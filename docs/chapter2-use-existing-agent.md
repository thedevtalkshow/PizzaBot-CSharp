# Chapter 2 — Using an Existing Agent

Chapter 1 created and registered the `ContosoPizzaBot` agent in Azure AI Foundry — its system prompt, RAG tool, function tool schema, and MCP connection all live there now. This chapter builds `PizzaBot.UseExistingAgent`, a console app that **looks up that agent by name and talks to it**.

The key insight: this project has no knowledge of how the agent was configured. It doesn't know about the vector store, the MCP server URI, or the system prompt. That configuration belongs to Foundry. Different apps — a web front-end, a mobile client, this console app — can all use the same agent without each one carrying a copy of its definition.

## What you'll build

- A console app that retrieves the `ContosoPizzaBot` agent from Foundry by name
- The same conversation loop from Chapter 1, including `PizzaCalculator` dispatch
- Minimal configuration — just an endpoint and an agent name

## Prerequisites

Chapter 1 complete and the `ContosoPizzaBot` agent exists in your Foundry project.

## 1. Create the project

From the `src/` directory:

```bash
dotnet new console -n PizzaBot.UseExistingAgent
cd PizzaBot.UseExistingAgent
```

Add the same three properties to `<PropertyGroup>` in the `.csproj`:

```xml
<EnablePreviewFeatures>true</EnablePreviewFeatures>
<Nullable>enable</Nullable>
<UserSecretsId>1a89e8d3-5a2b-441f-ac19-575312382091</UserSecretsId>
```

Add packages — identical to Chapter 1:

```bash
dotnet add package Azure.AI.Projects --version "1.2.*-*" --prerelease
dotnet add package Microsoft.Extensions.Configuration.Json
dotnet add package Microsoft.Extensions.Configuration.EnvironmentVariables
dotnet add package Microsoft.Extensions.Configuration.UserSecrets
```

## 2. Configuration

This project only needs two settings — the endpoint to reach Foundry and the name of the agent to use. Everything else is already stored in the agent definition.

Create `appsettings.json`:

```json
{
  "PizzaBot": {
    "ProjectEndpoint": "",
    "AgentName": "ContosoPizzaBot"
  }
}
```

Set `ProjectEndpoint` via user secrets (same value as Chapter 1):

```bash
dotnet user-secrets set "PizzaBot:ProjectEndpoint" "https://<resource>.services.ai.azure.com/api/projects/<project>"
```

Mark `appsettings.json` to copy to output in the `.csproj`:

```xml
<ItemGroup>
  <None Include="appsettings.json" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

| Setting | Where to set it |
|---|---|
| `ProjectEndpoint` | User secrets |
| `AgentName` | `appsettings.json` |

Wire up configuration in `Program.cs` — same layered pattern as Chapter 1 (user secrets > env vars > `appsettings.json`):

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
    ?? throw new InvalidOperationException("PizzaBot:ProjectEndpoint is required. Set it in appsettings.json, an environment variable (PizzaBot__ProjectEndpoint), or user secrets.");
string agentName = config["PizzaBot:AgentName"] ?? "ContosoPizzaBot";
```

## 3. Connect and retrieve the agent

```csharp
AIProjectClient projectClient = new(
    endpoint: new Uri(projectEndpoint),
    tokenProvider: new DefaultAzureCredential());

// Look up the agent by name. Throws if it doesn't exist.
AgentRecord agentRecord = projectClient.Agents.GetAgent(agentName);
Console.WriteLine($"Using existing agent: {agentRecord.Name} (id: {agentRecord.Id})\n");
```

`GetAgent` returns an `AgentRecord` — a lightweight reference that includes the agent's id and name but not its full definition. You don't need the definition here; Foundry already has it.

## 4. Start a conversation

Same pattern as Chapter 1, but passing `agentRecord` instead of `agentVersion.Name`:

```csharp
ProjectConversation conversation = projectClient.OpenAI.Conversations.CreateProjectConversation();
ProjectResponsesClient responseClient = projectClient.OpenAI.GetProjectResponsesClientForAgent(agentRecord, conversation);
```

## 5. The conversation loop

The loop is **identical** to Chapter 1. This is worth pausing on: the requires-action cycle doesn't change based on which agent you're talking to. The agent definition in Foundry tells the model that `CalculateNumberOfPizzasToOrder` exists and what arguments it takes — but where and how to execute it is entirely up to the client.

The agent says "call this function." Your code decides what that means.

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
        CreateResponseOptions responseOptions = new()
        {
            InputItems = { ResponseItem.CreateUserMessageItem(userInput) }
        };

        ResponseResult response;
        bool functionCalled;

        do
        {
            response = responseClient.CreateResponse(responseOptions);
            functionCalled = false;

            foreach (ResponseItem responseItem in response.OutputItems)
            {
                responseOptions.InputItems.Add(responseItem);

                if (responseItem is FunctionCallResponseItem functionCall)
                {
                    // The agent tells us which function to call — we execute it
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
}
```

You'll also need `PizzaCalculator.cs` — copy it from `PizzaBot.CreateFoundryAgent` or add it directly. It's the same file: the client always owns the function implementation.

## 6. Run it

```bash
dotnet run
```

Add to the solution:

```bash
dotnet sln ../PizzaOrdering.slnx add PizzaBot.UseExistingAgent.csproj
```

The output should immediately show which agent was found and its id, confirming the connection before you type a single message. If the agent name doesn't match what was created in Chapter 1, `GetAgent` will throw — a quick sanity check that the decoupling is working correctly.

---

> **Up next:** Chapter 3 replaces the manual requires-action loop with the Microsoft Agents SDK, which handles function dispatch automatically.
