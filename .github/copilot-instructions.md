# Contoso PizzaBot — Copilot Instructions

## What This Repo Is

This is a **C# workshop companion** to the Contoso PizzaBot Python workshop. It teaches developers how to build AI agents with **Azure AI Foundry** in idiomatic .NET. The code is intentionally workshop-quality: clear, minimal, and educational over clever or production-hardened.

## Project Map

| Project | Role | Key SDK |
|---|---|---|
| `PizzaBot.CreateFoundryAgent` | Creates/updates the full agent (system prompt + RAG + tool + MCP) | `Azure.AI.Projects` |
| `PizzaBot.UseExistingAgent` | Connects to an existing agent, drives conversation manually | `Azure.AI.Projects` |
| `PizzaBot.AgentFramework` | Same connect-and-converse but using the Agent Framework for automatic dispatch | `Microsoft.Agents.AI` |
| `Shared` | Shared models (`Pizza`, `Topping`, `Order`) used across projects | — |

The three main projects intentionally share the same agent definition in Azure AI Foundry. `CreateFoundryAgent` sets it up; the others consume it. This decoupling is a teaching point — keep it.

## Workshop Progression

When adding workshop steps, they follow the Python workshop chapter structure:
1. Basic agent (model + minimal system prompt)
2. System prompts from `instructions.txt`
3. RAG via `FileSearchTool` + vector store
4. Custom function tool (`PizzaCalculator`)
5. MCP integration (`McpTool`)

Each step should be independently runnable. Later steps extend earlier ones — never refactor backward.

## Code Patterns to Preserve

### Configuration
Always use the layered `IConfiguration` pattern with this priority (highest wins):
1. User secrets (`AddUserSecrets<Program>()`)
2. Environment variables (`AddEnvironmentVariables()`)
3. `appsettings.json` (`AddJsonFile(...)`)

Required values throw a clear `InvalidOperationException` at startup with the key name and how to set it. Never silently default a value that must come from the user.

### Authentication
Always use `DefaultAzureCredential` from `Azure.Identity`. Never hardcode tokens or connection strings.

### Conversation Loop
The manual loop pattern in `CreateFoundryAgent` and `UseExistingAgent` is intentional — it makes the `requires_action` cycle visible for teaching. Preserve the explicitness even if a helper could collapse it.

### Function Tools
`PizzaCalculator` is always executed client-side. The schema lives in Foundry (so the model knows the tool exists), but execution is always local. Make this distinction clear in READMEs and comments.

## Workshop-Quality Code Standards

- **Minimal over maximal**: Don't add abstractions that aren't needed to understand the concept being taught.
- **Comments explain *why*, not *what***: The code should be self-explanatory; comments surface the conceptual reason.
- **Top-level statements**: All projects use `Program.cs` with top-level statements — no explicit `Main` method.
- **No unnecessary using statements**: Keep imports clean; only what's used.
- **Readable output**: Console output uses `\n` for spacing between turns; keep it clean and scannable.

## READMEs

Every project has a README. When modifying a project, keep its README in sync. Each README must include:
- What it demonstrates (bullet list of concepts)
- How it works (code snippet of the key pattern)
- Running the sample (numbered steps)
- Configuration table (what's included vs. what you must supply)
- Libraries table (name, description, docs link)

## Data

- `knowledge/` — Contoso Pizza store markdown documents (used for RAG vector store)
- `data/` — `pizzas.json` and `toppings.json` (used by the MCP server and PizzaCalculator)
- `scripts/KnowledgeUpload.cs` — helper to upload knowledge docs to a vector store

Do not modify the knowledge documents without a good reason — they are the ground truth for RAG demos.

## What NOT to Do

- Don't add `try/catch` everywhere — the basic conversation loop's minimal error handling is intentional for readability
- Don't extract the conversation loop into a helper class in the workshop step projects — the explicitness is the lesson
- Don't add NuGet packages without updating the relevant README's Libraries table
- Don't commit secrets, connection strings, or personal Azure resource identifiers
