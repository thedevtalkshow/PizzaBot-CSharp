# PizzaBot.CreateFoundryAgent

This project demonstrates a fully featured PizzaBot agent built with the **Azure AI Projects SDK** (`Azure.AI.Projects`). It brings together all the major concepts from the [PizzaBot workshop](https://jolly-field-035345f1e.2.azurestaticapps.net/): system prompts, retrieval-augmented generation, custom tool calling, and MCP integration.

## What It Demonstrates

- **System prompts** — loading tailored agent instructions from `instructions.txt`
- **RAG (Retrieval-Augmented Generation)** — grounding the agent in pizza store data using the `FileSearchTool` and a pre-built vector store
- **Custom tool calling** — registering a `CalculateNumberOfPizzasToOrder` function tool and executing it locally when the agent requests it
- **MCP integration** — connecting the agent to a live Contoso Pizza MCP server for real menu and order management
- **Agent versioning** — using `CreateAgentVersion` to create or update an agent definition idempotently

> 🍕 **Order Dashboard**: View live orders at [https://ambitious-stone-0f6b9760f.2.azurestaticapps.net/](https://ambitious-stone-0f6b9760f.2.azurestaticapps.net/)

## How It Works

The agent definition is configured with three tools:

```csharp
PromptAgentDefinition agentDefinition = new(model: "gpt-4o")
{
    Instructions = instructions,
    Tools =
    {
        new FileSearchTool(["<vector-store-id>"]),   // RAG over pizza store documents
        pizzaCalculatorTool,                          // Custom function tool
        mcpTool                                       // Live MCP server integration
    }
};
```

When the agent invokes the pizza calculator, the client-side loop intercepts the function call, runs the local `PizzaCalculator` logic, and returns the result to the agent before it generates its final response.

## Running the Sample

1. Configure your project endpoint and other settings — use user secrets, environment variables, or `appsettings.json` (see below).
2. Replace the vector store ID with your own, or remove the `FileSearchTool` if you don't have one set up.
3. Update the MCP server URI to your own server, or remove that tool.
4. Customize `instructions.txt` as needed.
5. Authenticate using the Azure CLI (`az login`) or another `DefaultAzureCredential`-compatible method.
6. Run with:

```bash
dotnet run
```

## Configuration

Settings are loaded in this priority order (highest wins):

1. **User secrets** (recommended for local development)
2. **Environment variables**
3. **`appsettings.json`**

All settings marked required must be present for the app to run — it will throw a clear error at startup if any are missing.

### Included in the repository (`appsettings.json`)

These are shared workshop resources, so their values are already committed:

| Key | Value |
|---|---|
| `PizzaBot:McpServerUri` | Pre-configured Contoso Pizza MCP server |
| `PizzaBot:ModelDeploymentName` | `gpt-4o` |
| `PizzaBot:AgentName` | `ContosoPizzaBot` |

### Not included — you must supply these

These values belong to your Azure AI Foundry project and are not committed to the repository. Set them via user secrets or environment variables:

| Key | Required | Description |
|---|---|---|
| `PizzaBot:ProjectEndpoint` | ✅ | Your Azure AI Foundry project endpoint URL |
| `PizzaBot:VectorStoreId` | ✅ | Your vector store ID for file search (RAG) |

**User secrets** (run once from the project directory):
```bash
dotnet user-secrets set "PizzaBot:ProjectEndpoint" "https://<your-resource>.services.ai.azure.com/api/projects/<your-project>"
dotnet user-secrets set "PizzaBot:VectorStoreId" "vs_..."
```

**Environment variables** use `__` as the section separator:
```bash
PizzaBot__ProjectEndpoint=https://<your-resource>.services.ai.azure.com/api/projects/<your-project>
```

## Remarks

This project uses the **Responses API** (via `ProjectResponsesClient`) rather than the older Chat Completions API. The key difference is that the Responses API is stateful and has built-in support for tool calling — both of which are essential for agent scenarios. When the agent decides to call a tool (like the pizza calculator), the API signals that in its response, your code executes the function and sends the result back, and the loop continues until the agent produces a final answer for the user.

### Hosted tools vs. client-side tools

Not all tools work the same way:

- **File search and MCP** are *hosted* tools — Azure runs them on your behalf. The model calls them and gets results without your client code doing anything.
- **Function tools** (like the pizza calculator) are *client-side* — Foundry stores the schema (name, parameters, description) so the model knows the tool exists, but your code is always responsible for executing it. If you remove `PizzaCalculator` from this project, the agent will still try to call the function and the conversation will break.

### Verifying the agent definition in the portal

After running this project, you can inspect the created agent in the [Azure AI Foundry portal](https://ai.azure.com) under **Agents** in your project. You'll see `ContosoPizzaBot` listed with all three tools — file search, the pizza calculator function schema, and the MCP server — confirming everything was registered correctly.

The agent definition itself (model, instructions, tool schemas) is printed to the console on startup. If you prefer to define the agent once and just connect to it, see [PizzaBot.UseExistingAgent](../PizzaBot.UseExistingAgent/).

| Library | Description | Docs |
|---|---|---|
| `Azure.AI.Projects` | Azure AI Foundry SDK — create and manage agents, vector stores, file search, MCP tools, and the Responses API | [Learn](https://learn.microsoft.com/en-us/dotnet/api/overview/azure/ai.projects-readme) |
| `Azure.Identity` | Passwordless authentication to Azure services via `DefaultAzureCredential` | [Learn](https://learn.microsoft.com/en-us/dotnet/api/overview/azure/identity-readme) |
| `Microsoft.Extensions.Configuration` | Standard .NET configuration stack supporting JSON files, environment variables, and user secrets | [Learn](https://learn.microsoft.com/en-us/dotnet/core/extensions/configuration) |

> **Foundry Agent Service** is the underlying Azure platform these samples target. See the [service overview](https://learn.microsoft.com/en-us/azure/ai-services/agents/overview) for background on what agents, threads, and tools look like at the platform level.
