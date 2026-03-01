# PizzaBot.UseExistingAgent

This project demonstrates how to connect to an **already-provisioned agent** in Azure AI Foundry using the **Azure AI Projects SDK** (`Azure.AI.Projects`), rather than creating a new one from code.

This is useful when the agent definition (model, instructions, tools) is managed separately — for example, configured in the Azure AI Foundry portal or deployed by another process — and you just want to drive a conversation against it.

## What It Demonstrates

- **Connecting to an existing agent** — looking up a deployed agent by name using `projectClient.Agents.GetAgent`
- **Conversation management** — creating a `ProjectConversation` to maintain context across turns
- **Client-side tool execution** — the agent's tool definitions live in Foundry, but the actual function logic runs locally when the agent issues a function call
- **Separation of concerns** — agent configuration vs. agent execution are decoupled

## How It Works

```csharp
// Look up the agent that already exists in Foundry — no creation needed
AgentRecord agentRecord = projectClient.Agents.GetAgent(agentName);

// Open a conversation and get a response client scoped to that agent
ProjectConversation conversation = projectClient.OpenAI.Conversations.CreateProjectConversation();
ProjectResponsesClient responseClient = projectClient.OpenAI.GetProjectResponsesClientForAgent(agentRecord, conversation);
```

The client loop handles function call responses from the agent, executes the local `PizzaCalculator`, and feeds the result back — even though the tool was defined in Foundry.

## Running the Sample

1. Ensure the target agent (`ContosoPizzaBot`) has already been created in your Azure AI Foundry project — for example, by running the `PizzaBot.CreateFoundryAgent` project first.
2. Configure your project endpoint — use user secrets, environment variables, or `appsettings.json` (see below).
3. Authenticate using the Azure CLI (`az login`) or another `DefaultAzureCredential`-compatible method.
4. Run with:

```bash
dotnet run
```

## Configuration

Settings are loaded in this priority order (highest wins):

1. **User secrets** (recommended for local development)
2. **Environment variables**
3. **`appsettings.json`**

| Key | Required | Description |
|---|---|---|
| `PizzaBot:ProjectEndpoint` | ✅ | Azure AI Foundry project endpoint URL |
| `PizzaBot:AgentName` | | Name of the existing agent to connect to (default: `ContosoPizzaBot`) |

**User secrets** (run once from the project directory):
```bash
dotnet user-secrets set "PizzaBot:ProjectEndpoint" "https://<your-resource>.services.ai.azure.com/api/projects/<your-project>"
```

**Environment variables** use `__` as the section separator:
```bash
PizzaBot__ProjectEndpoint=https://<your-resource>.services.ai.azure.com/api/projects/<your-project>
```

## Remarks

This project uses the same **Responses API** (via `ProjectResponsesClient`) as `PizzaBot.CreateFoundryAgent`, but skips the agent creation step entirely — it just looks up the agent by name and starts a conversation. The agent's instructions, tools, and model are all already configured in Foundry.

This is a useful pattern when the agent definition is managed separately from the code that runs it — for example, if the agent is configured in the [Azure AI Foundry portal](https://ai.azure.com) or deployed by a different process. It also means multiple applications can share the same agent definition without duplicating configuration.

### Function tools still execute locally

Even though the agent definition lives in Foundry, **function tools always run in your code**. Foundry stores the function schema (name, parameters, description) so the model knows the tool exists and how to call it, but execution is always your client's responsibility. The `PizzaCalculator` in this project must be present and correct — if it's missing or doesn't handle the function call, the agent will stall waiting for a result that never comes.

File search and MCP tools work differently — those are *hosted* by Azure and run without any client-side code on your part.

### Verifying the agent definition

You can inspect the full agent definition — including all three registered tools — in the [Azure AI Foundry portal](https://ai.azure.com) under **Agents** in your project. This is the easiest way to confirm the tool schemas, instructions, and model are all configured as expected before running this project.

## Libraries

| Library | Description | Docs |
|---|---|---|
| `Azure.AI.Projects` | Azure AI Foundry SDK — connect to existing agents, manage conversations, and handle tool calls via the Responses API | [Learn](https://learn.microsoft.com/en-us/dotnet/api/overview/azure/ai.projects-readme) |
| `Azure.Identity` | Passwordless authentication to Azure services via `DefaultAzureCredential` | [Learn](https://learn.microsoft.com/en-us/dotnet/api/overview/azure/identity-readme) |
| `Microsoft.Extensions.Configuration` | Standard .NET configuration stack supporting JSON files, environment variables, and user secrets | [Learn](https://learn.microsoft.com/en-us/dotnet/core/extensions/configuration) |

> **Foundry Agent Service** is the underlying Azure platform these samples target. See the [service overview](https://learn.microsoft.com/en-us/azure/ai-services/agents/overview) for background on what agents, threads, and tools look like at the platform level.
