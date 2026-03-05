# PizzaBot.AgentFramework

This project demonstrates how to connect to an existing **Azure AI Foundry** persistent agent using the **Microsoft Agent Framework** (`Microsoft.Agents.AI`). Unlike the other projects in this repo, there is no client-side code for handling function tool call responses and submitting results back to the agent — the Agent Framework dispatches registered functions automatically. The result is the simplest conversation loop of any sample here: a single `RunAsync` call per turn, with no `requires_action` handling at all.

## What It Demonstrates

- **Connecting to an existing agent** — looks up a deployed Foundry persistent agent by name via `GetAIAgentAsync`
- **File search (RAG)** — the existing agent already has a vector store wired up; the client benefits without any extra configuration
- **MCP tool integration** — the existing agent already has the MCP server connected; no client-side setup required
- **Automatic function dispatch** — registers a local `PizzaCalculator` implementation via `AIFunctionFactory`; the Agent Framework intercepts tool call requests and executes the function automatically, with no manual dispatch code needed
- **Session management** — maintains server-side conversation history across turns via `AgentSession`

## Key Concepts

The Agent Framework abstracts away the underlying thread/run polling loop, including the `requires_action` cycle that `PizzaBot.UseExistingAgent` handles manually. You register function implementations once when connecting to the agent, then each conversation turn is a single `RunAsync` call:

```csharp
// Register the local function — the framework handles dispatch automatically
AITool tool = AIFunctionFactory.Create(PizzaCalculator.CalculateNumberOfPizzasToOrder);

// Connect to the existing agent and attach the function implementation
ChatClientAgent agent = await aiProjectClient.GetAIAgentAsync(agentName, tools: [tool]);

AgentSession session = await agent.CreateSessionAsync();

// Each turn is one call — no tool call/response loop needed
AgentResponse response = await agent.RunAsync(userInput, session);
```

## Running the Sample

1. Set your configuration values (see below) — at minimum `ProjectEndpoint` and `VectorStoreId`.
2. Authenticate using the Azure CLI: `az login`
3. Run with:

```bash
dotnet run
```

The target agent (`ContosoPizzaBot` by default) must already exist in your Foundry project. Run `PizzaBot.CreateFoundryAgent` first if you haven't set it up yet.

## Configuration

Settings are loaded in this priority order (highest wins):

1. **User secrets** (recommended for local development)
2. **Environment variables**
3. **`appsettings.json`**

### Included in this repo

| Key | Value |
|---|---|
| `PizzaBot:ModelDeploymentName` | `gpt-4o` |
| `PizzaBot:AgentName` | `ContosoPizzaBot` |
| `PizzaBot:McpServerUri` | Contoso MCP server (public endpoint from the workshop) |

### You must supply

| Key | Description |
|---|---|
| `PizzaBot:ProjectEndpoint` | Your Azure AI Foundry project endpoint, e.g. `https://<resource>.services.ai.azure.com/api/projects/<project>` |
| `PizzaBot:VectorStoreId` | The ID of the vector store containing the Contoso pizza menu documents |

**User secrets** (run once from the project directory):

```bash
dotnet user-secrets set "PizzaBot:ProjectEndpoint" "https://<your-resource>.services.ai.azure.com/api/projects/<your-project>"
dotnet user-secrets set "PizzaBot:VectorStoreId" "vs_..."
```

**Environment variables** use `__` as the section separator:

```bash
PizzaBot__ProjectEndpoint=https://<your-resource>.services.ai.azure.com/api/projects/<your-project>
PizzaBot__VectorStoreId=vs_...
```

## Libraries

| Library | Description | Docs |
|---|---|---|
| `Microsoft.Agents.AI.AzureAI` | Microsoft Agent Framework — high-level abstractions for building and orchestrating AI agents, with automatic function dispatch and session management | [Learn](https://learn.microsoft.com/agent-framework/overview/agent-framework-overview) |
| `Azure.Identity` | Passwordless authentication to Azure services via `DefaultAzureCredential` | [Learn](https://learn.microsoft.com/en-us/dotnet/api/overview/azure/identity-readme) |
| `Microsoft.Extensions.Configuration` | Standard .NET configuration stack supporting JSON files, environment variables, and user secrets | [Learn](https://learn.microsoft.com/en-us/dotnet/core/extensions/configuration) |

## Remarks

### How This Fits With the Other Samples

All three projects in this repo work with the **same agent definition**. `PizzaBot.CreateFoundryAgent` creates (or updates) the agent in Azure AI Foundry. `PizzaBot.UseExistingAgent` connects to it and drives a conversation, but still has to handle the function call loop manually — inspecting each response for `FunctionCallResponseItem`, executing the function, and submitting the result back. This project does the same connect-and-converse pattern but hands function dispatch off to the Agent Framework, which is why the conversation loop is so much simpler.

### Automatic Function Tool Dispatch

The Agent Framework eliminates the manual `requires_action` loop that `PizzaBot.UseExistingAgent` handles explicitly. When the model decides to call `CalculateNumberOfPizzasToOrder`, the framework:

1. Intercepts the tool call request from the Foundry run
2. Matches it to the `AITool` registered via `GetAIAgentAsync`
3. Executes the function locally
4. Submits the result back to Foundry and continues the run

All of this happens inside `RunAsync` — the conversation loop never sees it. The pizza calculator **always runs locally** — even though its schema is declared in the agent definition stored in Foundry, the C# code here must be running for it to work.

### Verifying the Agent in the Portal

The same agent used by `PizzaBot.CreateFoundryAgent` and `PizzaBot.UseExistingAgent` is used here. You can view it in the [Azure AI Foundry portal](https://ai.azure.com) under **Agents** in your project — its tool definitions (file search, MCP, and the pizza calculator schema) will be listed there.

