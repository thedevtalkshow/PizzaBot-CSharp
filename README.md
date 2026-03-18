# Contoso PizzaBot — C# Samples

This repository contains C# samples for the **Contoso PizzaBot** — a hands-on workshop originally written in Python that teaches you how to build intelligent AI agents using **Azure AI Foundry**. These samples translate every concept into idiomatic C# and .NET.

👉 [Original Workshop](https://jolly-field-035345f1e.2.azurestaticapps.net/) — the authoritative guide for what each concept means and why it matters.

## Start Here

This repo is a roadmap, not a linear path. Go as far as you want.

### Just want to explore Azure AI Foundry agents?

Start with **[PizzaBot.CreateFoundryAgent](src/PizzaBot.CreateFoundryAgent/)**. The workshop's hosted Contoso Pizza backend is already running — all you need is an Azure AI Foundry project. No local servers to stand up.

From there you can explore the other agent projects to see how the same agent gets consumed in different ways.

### Want to run the full stack locally?

Stand up **[PizzaApi](src/PizzaApi/)** and **[PizzaMcpServer](src/PizzaMcpServer/)** on your machine and point your Foundry agent at them instead of the hosted workshop backend. Add **[PizzaBot.Dashboard](src/PizzaBot.Dashboard/)** for a live order feed.

See [Running the Full Stack Locally](docs/local-stack.md) for setup instructions including tunnel options for Foundry connectivity.

### Want to build it yourself from scratch?

Step-by-step workshop chapters for building each project from an empty folder are available in [`docs/`](docs/).

---

## Agent Projects

These projects require an Azure AI Foundry project. The hosted Contoso Pizza backend is pre-configured — no local servers required to get started.

| Project | What it demonstrates |
|---|---|
| [PizzaBot.CreateFoundryAgent](src/PizzaBot.CreateFoundryAgent/) | Full-featured agent with system prompts, RAG (file search), a custom tool, and MCP integration |
| [PizzaBot.UseExistingAgent](src/PizzaBot.UseExistingAgent/) | Connecting to an already-provisioned Foundry agent — agent configuration and execution decoupled |
| [PizzaBot.AgentFramework](src/PizzaBot.AgentFramework/) | Same agent using the Microsoft Agent Framework — automatic tool dispatch replaces the manual loop |

## Backend & Dashboard

These projects run entirely locally. No Azure required.

| Project | What it demonstrates |
|---|---|
| [PizzaApi](src/PizzaApi/) | REST API for the Contoso Pizza ordering backend — the same data the workshop's hosted MCP server exposes |
| [PizzaMcpServer](src/PizzaMcpServer/) | MCP server wrapping the Pizza API — connects to GitHub Copilot, Claude, or a Foundry agent |
| [PizzaBot.Dashboard](src/PizzaBot.Dashboard/) | Blazor Server dashboard — live order feed and status overview |

Each project has its own README with run instructions and configuration details.

## Prerequisites

### Tools

- [.NET 10 SDK](https://dotnet.microsoft.com/download) or later
- [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli) — for agent projects only (`az login`)

### Azure Setup (agent projects only)

Follow the **Welcome & Setup** section of the [workshop](https://jolly-field-035345f1e.2.azurestaticapps.net/) to provision your environment. You will need:

- An **Azure subscription**
- An **Azure AI Foundry** project with a `gpt-4o` model deployment
- A **vector store** loaded with the Contoso Pizza store documents (covered in Chapter 3 of the workshop)

> If you have already completed the Python workshop, your existing Foundry project and vector store work here too.

## Workshop Chapters

Build each project from scratch with step-by-step instructions:

| Chapter | Project | What you'll build |
|---------|---------|-------------------|
| [Chapter 1](docs/chapter1-create-agent.md) | PizzaBot.CreateFoundryAgent | A Foundry agent with system prompt, RAG, function tool, and MCP integration |
| [Chapter 2](docs/chapter2-use-existing-agent.md) | PizzaBot.UseExistingAgent | Connect to an existing agent — configuration and execution decoupled |
| [Chapter 3](docs/chapter3-agent-framework.md) | PizzaBot.AgentFramework | Same agent using the Agent Framework — tool dispatch automatic |
| [Chapter 4](docs/chapter4-pizza-api.md) | PizzaApi + Shared | Local REST API with in-memory data and order status progression |
| [Chapter 5](docs/chapter5-mcp-server.md) | PizzaMcpServer | MCP server wrapping the API — connect to Copilot, Claude, or Foundry |
| [Chapter 6](docs/chapter6-dashboard.md) | PizzaBot.Dashboard | Blazor Server dashboard with live order feed and timer-based polling |

## Additional Guides

| Guide | Description |
|-------|-------------|
| [Running the full local stack](docs/local-stack.md) | Run PizzaApi and PizzaMcpServer locally and connect a Foundry agent or GitHub Copilot via tunnel |
