# PizzaMcpServer

A local MCP server that wraps the **Contoso Pizza API** and exposes its operations as tools that any MCP-compatible AI client can call — GitHub Copilot, Claude, or an Azure AI Foundry agent.

## What It Demonstrates

- Exposing HTTP API operations as MCP tools using `ModelContextProtocol.AspNetCore`
- Registering tools with `[McpServerToolType]` / `[McpServerTool]` / `[Description]` attributes
- Supporting both Streamable HTTP and legacy SSE transports via a single `MapMcp` call

## Running the Server

The Pizza API must be running on port 7071 first. Then:

```bash
dotnet run
```

The MCP server listens on **`http://localhost:3000`**.

## Transport Endpoints

`MapMcp("/mcp")` automatically registers both transports:

| Transport | URL | Used by |
|-----------|-----|---------|
| Streamable HTTP | `http://localhost:3000/mcp` | Azure AI Foundry, GitHub Copilot, Claude |
| SSE (legacy) | `http://localhost:3000/mcp/sse` | Older clients — fallback only |

Use `/mcp` for everything. The SSE endpoint is there if you hit an older client that requires it.

## Available Tools

| Tool | Description |
|------|-------------|
| `GetPizzas` | Full pizza menu |
| `GetPizzaById` | Single pizza by ID |
| `GetToppings` | All toppings, with optional category filter |
| `GetToppingById` | Single topping by ID |
| `GetToppingCategories` | Distinct topping categories |
| `GetOrders` | Orders, with optional userId / status / time filters |
| `GetOrderById` | Single order by ID |
| `PlaceOrder` | Create a new order |
| `DeleteOrderById` | Cancel a pending order |

## Connecting to GitHub Copilot

Add to `.mcp.json` in the solution root (already present):

```json
{
  "servers": {
    "PizzaMcpServer": {
      "url": "http://localhost:3000/mcp"
    }
  }
}
```

Then open Copilot Chat in agent mode and ask about the menu or place an order.

## Connecting to an Azure AI Foundry Agent

Azure AI Foundry calls the MCP server from the cloud, so localhost won't work. Expose the server via a public tunnel first:

1. In VS Code: **View → Ports → Forward a Port → 3000 → set visibility to Public**
2. Use the public HTTPS URL as `McpServerUri` in `appsettings.json` for `PizzaBot.CreateFoundryAgent`
3. The MCP endpoint is `https://<tunnel-url>/mcp`

## Libraries

| Library | Description | Docs |
|---------|-------------|------|
| `ModelContextProtocol.AspNetCore` | Official C# MCP SDK — HTTP transport, tool registration, SSE support | [NuGet](https://www.nuget.org/packages/ModelContextProtocol.AspNetCore) |
