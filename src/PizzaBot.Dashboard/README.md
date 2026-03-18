# PizzaBot.Dashboard

A Blazor Server dashboard that shows a live view of Contoso Pizza orders. It polls the **[PizzaApi](../PizzaApi/)** every 10 seconds and displays current order status, counts, and details in a simple table.

## What It Demonstrates

- **Blazor Server with interactive render mode** — `@rendermode InteractiveServer` enables real-time UI updates over a SignalR connection
- **Typed `HttpClient` via dependency injection** — `OrdersService` is registered as a typed client with the API base URL wired up in `Program.cs`
- **Timer-based polling** — a `System.Threading.Timer` started in `OnAfterRender` refreshes data every 10 seconds without blocking the UI thread
- **CSS isolation** — component-specific styles live in `Home.razor.css` and are scoped automatically by the Blazor compiler
- **Graceful error handling** — when the API is unreachable, the dashboard shows an error banner rather than crashing or silently displaying stale data

## Running the Dashboard

PizzaApi must be running before you start the dashboard.

1. Start PizzaApi (from its project directory):

```bash
dotnet run
```

2. Start the dashboard (from this project directory):

```bash
dotnet run
```

3. Open **`http://localhost:4280`** in your browser.

The dashboard auto-refreshes every 10 seconds. Use the PizzaApi endpoints or the PizzaMcpServer to place orders and watch them appear.

## Configuration

| Key | Default | Description |
|---|---|---|
| `PizzaApi:BaseUrl` | `http://localhost:7071` | Base URL of the PizzaApi instance to poll |

Override the default via user secrets or an environment variable if you are running the API on a different port or host:

```bash
dotnet user-secrets set "PizzaApi:BaseUrl" "http://localhost:7071"
```

## Libraries

| Library | Description | Docs |
|---|---|---|
| `Microsoft.AspNetCore.Components` (built-in) | Blazor Server — interactive server-side rendering with SignalR | [Learn](https://learn.microsoft.com/en-us/aspnet/core/blazor/) |
