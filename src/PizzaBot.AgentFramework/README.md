# PizzaBot.AgentFramework

This project demonstrates how to build a fully-featured conversational pizza ordering agent using the **Microsoft Agent Framework** (`Microsoft.Agents.AI`), which provides a higher-level abstraction over the Azure AI Foundry Persistent Agents service.

It creates a new persistent agent named **ContosoPizzaBotWithAgentFramework** that has the same capabilities as the agent built in `PizzaBot.CreateFoundryAgent` — system prompts, RAG-based file search, an MCP-connected order system, and a local pizza calculator function — but implemented using the Agent Framework SDK instead of `Azure.AI.Projects`.

## What It Demonstrates

- **Agent provisioning** — creating a persistent agent in Foundry with file search, MCP, and function tool definitions via `Administration.CreateAgentAsync`
- **Find-or-create pattern** — reuses an existing agent by name instead of creating a duplicate on every run
- **File search (RAG)** — queries the Contoso pizza menu vector store hosted in Foundry
- **MCP tool integration** — connects to the Contoso order management MCP server hosted in Azure
- **Automatic function dispatch** — registers a local `PizzaCalculator` implementation via `AIFunctionFactory`; the Agent Framework handles the tool call/response loop automatically
- **Session management** — maintains server-side conversation history across turns via `AgentSession`

## Key Concepts

The Agent Framework's `AIAgent` + `AgentSession` model abstracts away the underlying thread/run polling loop. Instead of manually checking run status and submitting tool outputs, you register function implementations once and call `RunAsync`:

```csharp
// Register the local function implementation — no manual dispatch loop needed
AIFunction pizzaCalcFunction = AIFunctionFactory.Create(PizzaCalculator.CalculateNumberOfPizzasToOrder);

ChatClientAgentRunOptions runOptions = new()
{
    ChatOptions = new()
    {
        Tools = [pizzaCalcFunction],
        RawRepresentationFactory = (_) => new ThreadAndRunOptions()
        {
            ToolResources = new MCPToolResource(serverLabel: "contoso-pizza-mcp")
            {
                RequireApproval = new MCPApproval("never"),
            }.ToToolResources()
        }
    }
};

AgentResponse response = await agent.RunAsync(userInput, session, runOptions);
```

## Running the Sample

1. Set your configuration values (see below) — at minimum `ProjectEndpoint` and `VectorStoreId`.
2. Authenticate using the Azure CLI: `az login`
3. Run with:

```bash
dotnet run
```

The agent will be created in Foundry on first run, then reused on subsequent runs.

## Configuration

Settings are loaded in this priority order (highest wins):

1. **User secrets** (recommended for local development)
2. **Environment variables**
3. **`appsettings.json`**

### Included in this repo

| Key | Value |
|---|---|
| `PizzaBot:ModelDeploymentName` | `gpt-4o` |
| `PizzaBot:AgentName` | `ContosoPizzaBotWithAgentFramework` |
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
| `Microsoft.Agents.AI.AzureAI.Persistent` | Microsoft Agent Framework — high-level abstractions for building, orchestrating, and deploying AI agents backed by the Foundry Persistent Agents service | [Learn](https://learn.microsoft.com/agent-framework/overview/agent-framework-overview) |
| `Azure.Identity` | Passwordless authentication to Azure services via `DefaultAzureCredential` | [Learn](https://learn.microsoft.com/en-us/dotnet/api/overview/azure/identity-readme) |
| `Microsoft.Extensions.Configuration` | Standard .NET configuration stack supporting JSON files, environment variables, and user secrets | [Learn](https://learn.microsoft.com/en-us/dotnet/core/extensions/configuration) |

## Remarks

### Persistent Agents vs. Agent Versions — Two Different APIs

Azure AI Foundry exposes **two distinct agent systems**, and the SDK you choose determines which one you use:

- **`Azure.AI.Projects`** (used in `PizzaBot.CreateFoundryAgent` and `PizzaBot.UseExistingAgent`) targets the **Agent Versions API** — a newer system built on the Responses API. Agents created here appear as versioned Agent Versions in the Foundry portal.

- **`Microsoft.Agents.AI.AzureAI.Persistent`** (this project) targets the **Persistent Agents API** — an earlier system modeled after the OpenAI Assistants API, using a thread/run model. Agents created here appear under the Agents section of the Foundry portal.

These are separate namespaces in the portal. **An agent created by this project cannot be used by `PizzaBot.UseExistingAgent`**, and vice versa.

### Automatic Function Tool Dispatch

The Agent Framework eliminates the manual "requires_action" polling loop. When the model decides to call `CalculateNumberOfPizzasToOrder`, the framework:

1. Intercepts the tool call request from the Foundry run
2. Matches it to the `AIFunction` registered in `ChatOptions.Tools`
3. Executes the function locally
4. Submits the result back to Foundry and continues the run

This means the pizza calculator **always runs locally** — even though it is declared in the agent definition stored in Foundry, the C# code here must be running for it to work.

### Verifying the Agent in the Portal

After the first run, you can view the agent in the [Azure AI Foundry portal](https://ai.azure.com). Navigate to your project, then look under **Agents** (the Persistent Agents section) and find `ContosoPizzaBotWithAgentFramework`. Its tool definitions — file search, MCP, and the pizza calculator schema — will be listed there.

