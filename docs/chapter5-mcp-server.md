# Chapter 5 — Building the MCP Server

In this chapter you'll build `PizzaMcpServer` from scratch — an MCP server that wraps the Pizza API as tools any MCP client can call: GitHub Copilot, Claude, or an Azure AI Foundry agent.

The PizzaApi from Chapter 4 must be running before you start.

## What You'll Build

The server exposes **9 tools** that map directly to Pizza API endpoints:

| Tool | What it does |
|---|---|
| `GetPizzas` | List all menu pizzas |
| `GetPizzaById` | Get a single pizza |
| `GetToppings` | List toppings, with optional category filter |
| `GetToppingById` | Get a single topping |
| `GetToppingCategories` | List all topping categories |
| `GetOrders` | List orders, with optional userId/status/time filters |
| `GetOrderById` | Get a single order |
| `PlaceOrder` | Submit a new order |
| `DeleteOrderById` | Cancel a pending order |

Both **Streamable HTTP** and **SSE** transports are registered automatically by a single `MapMcp` call — so the same server works with GitHub Copilot, Claude Desktop, and Azure AI Foundry agents without any extra configuration.

---

## 1. Create the Project

Use `dotnet new web` rather than `dotnet new mcp-server`. The MCP server template adds NuGet packaging configuration intended for distributing a server as a package — we're running this locally, so that's just noise.

```
dotnet new web -n PizzaMcpServer
cd PizzaMcpServer
```

---

## 2. Add NuGet Packages

```
dotnet add package ModelContextProtocol.AspNetCore --version "1.1.0"
dotnet add package Microsoft.Extensions.Hosting
```

`ModelContextProtocol.AspNetCore` brings in the MCP server runtime and the ASP.NET Core integration (`MapMcp`, `AddMcpServer`, transport middleware). `Microsoft.Extensions.Hosting` ensures the full hosting abstractions are available.

---

## 3. Configure the Project File

Open `PizzaMcpServer.csproj` and make sure it looks like this — minimal, no packaging config:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Hosting" Version="9.0.4" />
    <PackageReference Include="ModelContextProtocol.AspNetCore" Version="1.1.0" />
  </ItemGroup>

</Project>
```

---

## 4. Create `Tools/PizzaTools.cs`

Create a `Tools/` folder and add `PizzaTools.cs`. This class is the entire MCP surface area of the server — every tool is a method here.

```csharp
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Net.Http.Json;

namespace PizzaMcpServer.Tools
{
    // [McpServerToolType] marks this class for automatic tool discovery during AddMcpServer() setup.
    // The MCP runtime scans for this attribute and registers each [McpServerTool] method as a callable tool.
    [McpServerToolType]
    internal class PizzaTools(IHttpClientFactory httpClientFactory)
    {
        private HttpClient Client => httpClientFactory.CreateClient("PizzaApi");

        // All API calls flow through here. Using a single helper keeps error handling consistent
        // and makes each tool method a clean one-liner.
        private async Task<string> FetchAsync(string url, HttpMethod? method = null, object? body = null)
        {
            var request = new HttpRequestMessage(method ?? HttpMethod.Get, url);
            if (body != null)
                request.Content = JsonContent.Create(body);
            var response = await Client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
                throw new Exception($"API error {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
            if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
                return "Operation completed successfully. No content returned.";
            return await response.Content.ReadAsStringAsync();
        }

        // [McpServerTool] registers this method as a callable MCP tool.
        // [Description] sets the tool description the AI model sees when deciding which tool to call.
        [McpServerTool, Description("Get a list of all pizzas on the menu")]
        public Task<string> GetPizzas() => FetchAsync("/api/pizzas");

        [McpServerTool, Description("Get a specific pizza by its ID")]
        public Task<string> GetPizzaById([Description("ID of the pizza to retrieve")] string id)
            => FetchAsync($"/api/pizzas/{id}");

        [McpServerTool, Description("Get a list of all toppings in the menu")]
        public Task<string> GetToppings([Description("Category of toppings to filter by (can be empty)")] string? category = null)
            => FetchAsync($"/api/toppings?category={category ?? ""}");

        [McpServerTool, Description("Get a specific topping by its ID")]
        public Task<string> GetToppingById([Description("ID of the topping to retrieve")] string id)
            => FetchAsync($"/api/toppings/{id}");

        [McpServerTool, Description("Get a list of all topping categories")]
        public Task<string> GetToppingCategories() => FetchAsync("/api/toppings/categories");

        [McpServerTool, Description("Get a list of orders in the system")]
        public Task<string> GetOrders(
            [Description("Filter orders by user ID")] string? userId = null,
            [Description("Filter by order status. Comma-separated list allowed.")] string? status = null,
            [Description("Filter orders created in the last X minutes or hours (e.g. '60m', '2h')")] string? last = null)
        {
            var qs = new List<string>();
            if (!string.IsNullOrEmpty(userId)) qs.Add($"userId={Uri.EscapeDataString(userId)}");
            if (!string.IsNullOrEmpty(status)) qs.Add($"status={Uri.EscapeDataString(status)}");
            if (!string.IsNullOrEmpty(last)) qs.Add($"last={Uri.EscapeDataString(last)}");
            var url = qs.Count > 0 ? $"/api/orders?{string.Join("&", qs)}" : "/api/orders";
            return FetchAsync(url);
        }

        [McpServerTool, Description("Get a specific order by its ID")]
        public Task<string> GetOrderById([Description("ID of the order to retrieve")] string id)
            => FetchAsync($"/api/orders/{id}");

        [McpServerTool, Description("Place a new order with pizzas (requires userId)")]
        public Task<string> PlaceOrder(
            [Description("ID of the user placing the order")] string userId,
            [Description("List of items: each with pizzaId, quantity, and optional extraToppingIds")] PlaceOrderItem[] items,
            [Description("Optional nickname for the order")] string? nickname = null)
            => FetchAsync("/api/orders", HttpMethod.Post, new { userId, nickname, items });

        [McpServerTool, Description("Cancel an order if it has not yet been started (status must be 'pending', requires userId)")]
        public Task<string> DeleteOrderById(
            [Description("ID of the order to cancel")] string id,
            [Description("ID of the user that placed the order")] string userId)
            => FetchAsync($"/api/orders/{id}?userId={Uri.EscapeDataString(userId)}", HttpMethod.Delete);
    }
}

// Defined outside the class so it's a top-level type in the namespace.
// The MCP runtime serializes/deserializes this as the structured input for PlaceOrder.
public record PlaceOrderItem(
    [property: Description("ID of the pizza")] string PizzaId,
    [property: Description("Quantity of the pizza")] int Quantity,
    [property: Description("List of extra topping IDs")] string[]? ExtraToppingIds = null
);
```

### Key concepts

**`[McpServerToolType]`** — applied to the class, tells the MCP runtime to scan it for tool methods during startup. Without it, `WithTools<PizzaTools>()` in `Program.cs` would have nothing to register.

**`[McpServerTool]`** — applied to each method. Combined with `[Description]`, these attributes populate the tool schema that gets sent to the AI model. The description is what the model reads when choosing which tool to invoke.

**`[Description]` on parameters** — these become the parameter descriptions in the JSON schema. Writing these well is the difference between a model that uses the tool correctly and one that guesses.

**`Task<string>` return type** — every tool returns raw JSON from the API. The model receives and interprets the JSON directly. This keeps the tools thin and lets the model do the reasoning work.

**`PlaceOrderItem` record** — the MCP runtime uses reflection to generate a JSON schema from this type. Putting `[property: Description(...)]` on primary constructor parameters flows through to the schema.

---

## 5. Write `Program.cs`

Replace the generated `Program.cs` with:

```csharp
using PizzaMcpServer.Tools;

var builder = WebApplication.CreateBuilder(args);

var pizzaApiUrl = builder.Configuration["PizzaApi:BaseUrl"] ?? "http://localhost:7071";

// Named HttpClient — the name "PizzaApi" matches the CreateClient call in PizzaTools.
builder.Services.AddHttpClient("PizzaApi", client =>
{
    client.BaseAddress = new Uri(pizzaApiUrl);
    client.DefaultRequestHeaders.Add("Accept", "application/json");
});

// Redirect logs to stderr — stdout is reserved for the MCP protocol stream.
// For stdio transport, any log output on stdout corrupts the message framing.
// Keeping this habit for HTTP transport too means the server behaves identically in both modes.
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services
    .AddMcpServer()       // registers the MCP server runtime
    .WithHttpTransport()  // enables Streamable HTTP and SSE transports
    .WithTools<PizzaTools>(); // scans PizzaTools for [McpServerTool] methods

var app = builder.Build();

// Registers both /mcp (Streamable HTTP) and /mcp/sse (legacy SSE) under the /mcp prefix.
app.MapMcp("/mcp");

app.Run();
```

### What each line does

**Named `HttpClient` "PizzaApi"** — `IHttpClientFactory.CreateClient("PizzaApi")` in `PizzaTools` resolves this exact registration. Using a named client rather than a typed one keeps `PizzaTools` clean — it doesn't have to be the generic type parameter anywhere in DI registration.

**Logging to stderr** — the MCP stdio transport uses stdout for protocol messages; any logging written to stdout corrupts the binary/JSON framing. Even though we're using HTTP transport here, routing logs to stderr is good discipline and makes the server stdio-compatible without changes.

**`.AddMcpServer().WithHttpTransport().WithTools<PizzaTools>()`** — the entire MCP server setup in three chained calls. `WithTools<T>` triggers the attribute scan.

**`app.MapMcp("/mcp")`** — registers two endpoints:
- `POST /mcp` — Streamable HTTP (the current standard)
- `GET /mcp/sse` + `POST /mcp/message` — legacy SSE transport

One call, both transports.

---

## 6. The Two Transports

`MapMcp` registers both transports automatically.

**Streamable HTTP (`POST /mcp`)** is the current standard. Azure AI Foundry, GitHub Copilot, and Claude all support it. Use this for anything new.

**SSE (`GET /mcp/sse`)** is the legacy transport from the earlier MCP specification. It opens a long-lived SSE stream for server-to-client messages and uses a separate `POST /mcp/message` endpoint for client-to-server messages. It still works and is useful for clients that haven't migrated yet.

Because `MapMcp` registers both, you don't have to choose — clients will use whichever they support.

---

## 7. Connect to GitHub Copilot

The `.mcp.json` at the solution root is already configured:

```json
{
  "servers": {
    "PizzaMcpServer": {
      "url": "http://localhost:3000/mcp"
    }
  }
}
```

Start the server, open Copilot Chat in **agent mode** (the `@` dropdown → select your workspace agent), and ask:

> What pizzas are available?

Copilot will call `GetPizzas` and return the menu.

---

## 8. Connect to Azure AI Foundry

Foundry calls the MCP server from Azure, so `localhost` won't resolve. You need to expose the server over a tunnel.

See **`docs/local-stack.md`** for the full tunnel setup: VS Code port forwarding, Visual Studio dev tunnels, and ngrok are all covered there.

Once you have a public URL, set it in `PizzaBot.CreateFoundryAgent/appsettings.json`:

```json
{
  "McpServerUri": "https://your-tunnel-url/mcp"
}
```

---

## 9. Run It

```
dotnet run
```

The server starts on port 3000 by default (set in `launchSettings.json`). You should see:

```
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: http://localhost:3000
```

Add to the solution:

```
dotnet sln ../PizzaOrdering.slnx add PizzaMcpServer/PizzaMcpServer.csproj
```
