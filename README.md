# Contoso PizzaBot — C# Samples

This repository contains C# implementations of the **Contoso PizzaBot** — a hands-on workshop originally written in Python that teaches you how to build intelligent, domain-specific AI agents using **Azure AI Foundry**.

The workshop takes you from zero to a fully working pizza ordering assistant, step by step:

- 🤖 **Creating an AI agent** with a system prompt that shapes its personality and behavior
- 📚 **Grounding it in real data** using Retrieval-Augmented Generation (RAG) and a vector store
- 🔧 **Extending it with custom tools** like a pizza calculator function
- 🔌 **Connecting it to live services** via an MCP server for real menu and order management

These samples translate every concept from the original Python workshop into idiomatic C# and .NET, so you can follow along or use them as a reference without leaving the .NET ecosystem.

> **Note:** This is not a chapter-by-chapter port of the workshop. A step-by-step C# equivalent of the workshop is planned for the future. Instead, `PizzaBot.CreateFoundryAgent` implements the same end goal as the workshop — a fully configured agent with system prompts, RAG, a custom tool, and MCP — in a single project. `PizzaBot.UseExistingAgent` then demonstrates how to connect to and use that hosted agent from a separate application.

## Original Workshop

👉 [Contoso PizzaBot Workshop](https://jolly-field-035345f1e.2.azurestaticapps.net/)

The workshop is the authoritative guide for what each concept means and why it matters. This repo is the C# companion to it — set up your Azure environment by following the workshop, then run the samples here.

## Projects

| Project | SDK | What it demonstrates |
|---|---|---|
| [PizzaBot.CreateFoundryAgent](src/PizzaBot.CreateFoundryAgent/) | `Azure.AI.Projects` | Full-featured agent with system prompts, RAG (file search), a custom tool, and MCP integration |
| [PizzaBot.UseExistingAgent](src/PizzaBot.UseExistingAgent/) | `Azure.AI.Projects` | Connecting to an already-provisioned Foundry agent rather than creating a new one |

Each project has its own README explaining what it demonstrates and how to configure it.

## Prerequisites

Before running any sample, make sure you have the following in place.

### Tools

- [.NET 10 SDK](https://dotnet.microsoft.com/download) or later
- [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli) for authentication (`az login`)

### Azure Setup

Follow the **Welcome & Setup** section of the [workshop](https://jolly-field-035345f1e.2.azurestaticapps.net/) to get your Azure environment ready. You will need:

- An **Azure subscription**
- An **Azure AI Foundry** project with a `gpt-4o` model deployment
- A **vector store** loaded with the Contoso Pizza store documents (covered in Chapter 3 of the workshop)

> The workshop provides step-by-step instructions for all of the above. If you have already completed the Python workshop, your existing Foundry project and vector store will work here too.

## Getting Started

1. Clone the repository.
2. Follow the setup instructions in the README for the project you want to run.
3. Set your Foundry project endpoint and vector store ID via `dotnet user-secrets` (details in each project's README).
4. Authenticate with Azure: `az login`
5. Run from the project directory: `dotnet run`
