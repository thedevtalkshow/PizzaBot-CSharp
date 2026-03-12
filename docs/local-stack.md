# Running the Full Local Stack

By default the PizzaBot agent samples connect to the **hosted Contoso Pizza MCP server** provided by the workshop. If you want to run everything locally — your own API, your own MCP server, and agents pointing at them — follow these steps.

## What you'll be running

```
PizzaApi          → http://localhost:7071   (REST API)
PizzaMcpServer    → http://localhost:3000   (MCP server)
PizzaBot.*        → connects via tunnel     (Azure AI Foundry agent)
```

## Step 1 — Add the data files

The API loads its menu data from a `data/` folder at the solution root. This folder is not committed to the repository. Copy or download `pizzas.json` and `toppings.json` into:

```
pizzabot/
  data/
    pizzas.json
    toppings.json
```

If the files are missing, the API will start but return empty menus. You can get the data files from the [original workshop repository](https://github.com/Azure-Samples/pizza-mcp-agents/tree/main/src/pizza-api/data).

## Step 2 — Start the Pizza API

```bash
cd src/PizzaApi
dotnet run
```

Verify it's working:

```bash
curl http://localhost:7071/api/pizzas
```

You should get back a list of 16 pizzas.

## Step 3 — Start the MCP Server

In a separate terminal:

```bash
cd src/PizzaMcpServer
dotnet run
```

The MCP server proxies all requests to the Pizza API at `http://localhost:7071`.

Verify it's working:

```bash
curl http://localhost:3000/
```

## Step 4 — Expose the MCP server publicly (required for Foundry)

Azure AI Foundry calls your MCP server from the cloud, so `localhost` is unreachable. You need a public tunnel.

**Option A — VS Code Ports panel (recommended)**

1. Open VS Code
2. Go to **View → Ports** (or the Ports tab in the terminal panel)
3. Click **Forward a Port**, enter `3000`
4. Right-click the forwarded port and set visibility to **Public**
5. Copy the HTTPS URL shown — it will look like `https://abc123-3000.uks1.devtunnels.ms`

> The Public visibility step is critical. Azure cannot authenticate with your personal dev tunnel account, so a Private tunnel will fail silently.

**Option B — ngrok**

```bash
ngrok http 3000
```

Copy the `https://` forwarding URL from the ngrok output.

## Step 5 — Point the agent at your local MCP server

In `src/PizzaBot.CreateFoundryAgent/appsettings.json`, update the MCP server URI:

```json
{
  "PizzaBot": {
    "McpServerUri": "https://<your-tunnel-url>/mcp/sse"
  }
}
```

Or set it via user secrets to avoid committing the URL:

```bash
cd src/PizzaBot.CreateFoundryAgent
dotnet user-secrets set "PizzaBot:McpServerUri" "https://<your-tunnel-url>/mcp/sse"
```

> The SSE endpoint (`/mcp/sse`) is what Azure AI Foundry uses. The Streamable HTTP endpoint (`/mcp`) is for Copilot and Claude.

## Step 6 — Run the agent

```bash
cd src/PizzaBot.CreateFoundryAgent
dotnet run
```

The agent will be created (or updated) in your Azure AI Foundry project pointing at your local MCP server. Any orders placed through the agent will appear in your local API's in-memory store.

## Using with GitHub Copilot instead

If you want to test the MCP server with GitHub Copilot rather than a Foundry agent, no tunnel is needed. Add or update `.mcp.json` in the solution root:

```json
{
  "servers": {
    "PizzaMcpServer": {
      "url": "http://localhost:3000/mcp"
    }
  }
}
```

Then open Copilot Chat in agent mode and ask about the menu or place an order directly.
