# Chapter 6 — Building the Dashboard

In this chapter you'll build `PizzaBot.Dashboard` — a Blazor Server app that shows a live order feed with auto-refresh.

The PizzaApi from Chapter 4 must be running before you start.

## What You'll Build

A single-page dashboard that:

- Shows all active orders in a sortable table
- Displays total and active order counts from the API's status endpoint
- Auto-refreshes every 10 seconds without user interaction
- Highlights order status with color-coded badges
- Shows an error banner if the API is unreachable, then clears it automatically when the API comes back

---

## 1. Create the Project

```
dotnet new blazor -n PizzaBot.Dashboard --interactivity Server --empty
cd PizzaBot.Dashboard
```

`--interactivity Server` configures the app for Blazor Server mode — interactive components are rendered server-side and communicate with the browser over a SignalR connection. `--empty` skips the Counter and Weather sample pages so you start clean.

Add the Shared project reference so you can use the `OrderResponse` and `StatusResponse` models:

```
dotnet add reference ../Shared/Shared.csproj
```

---

## 2. Create `Services/OrdersService.cs`

Create a `Services/` folder and add `OrdersService.cs`:

```csharp
using System.Text.Json;
using Shared.Models;

namespace PizzaBot.Dashboard.Services;

public class OrdersService(HttpClient httpClient)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<List<OrderResponse>> GetOrdersAsync(string? status = null)
    {
        var url = string.IsNullOrEmpty(status) ? "/api/orders" : $"/api/orders?status={status}";
        return await httpClient.GetFromJsonAsync<List<OrderResponse>>(url, JsonOptions) ?? [];
    }

    public async Task<StatusResponse?> GetStatusAsync()
    {
        return await httpClient.GetFromJsonAsync<StatusResponse>("/api", JsonOptions);
    }
}
```

This is a **typed HttpClient** — the `HttpClient` is injected via the primary constructor and pre-configured with the base URL when registered in DI. Methods throw on non-success status codes, which the component catches and surfaces as an error message.

---

## 3. Write `Program.cs`

Replace the generated `Program.cs` with:

```csharp
using PizzaBot.Dashboard.Components;
using PizzaBot.Dashboard.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var pizzaApiUrl = builder.Configuration["PizzaApi:BaseUrl"] ?? "http://localhost:7071";

// Typed HttpClient: DI injects an HttpClient with this base address into OrdersService's constructor.
builder.Services.AddHttpClient<OrdersService>(client =>
{
    client.BaseAddress = new Uri(pizzaApiUrl);
});

var app = builder.Build();

app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
```

`AddInteractiveServerComponents()` and `AddInteractiveServerRenderMode()` work as a pair — the first registers the services, the second registers the endpoint middleware that handles the SignalR circuit. Both are required for `@rendermode InteractiveServer` to function.

---

## 4. Update `appsettings.json`

Add the PizzaApi base URL so it can be overridden per environment:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "PizzaApi": {
    "BaseUrl": "http://localhost:7071"
  }
}
```

---

## 5. Build `Components/Pages/Home.razor`

This is the entire dashboard. Replace the generated `Home.razor` with:

```razor
@page "/"
@rendermode InteractiveServer
@using Shared.Models
@using PizzaBot.Dashboard.Services
@inject OrdersService OrdersService
@implements IDisposable

<PageTitle>Pizza Dashboard</PageTitle>

<div class="dashboard">
    <h1>🍕 Contoso Pizza Dashboard</h1>

    @if (_error != null)
    {
        <div class="error-banner">⚠️ Could not reach the Pizza API: @_error</div>
    }

    @if (_status != null)
    {
        <div class="stats">
            <span class="stat">Active Orders: <strong>@_status.ActiveOrders</strong></span>
            <span class="stat">Total Orders: <strong>@_status.TotalOrders</strong></span>
        </div>
    }

    <div class="refresh-info">Auto-refreshes every 10s · Last updated: @_lastUpdated.ToString("HH:mm:ss")</div>

    @if (_orders.Count == 0)
    {
        <div class="empty-state"><p>No orders yet.</p></div>
    }
    else
    {
        <table class="orders-table">
            <thead>
                <tr>
                    <th>Order</th><th>Status</th><th>Items</th><th>Total</th><th>Created</th><th>Est. Ready</th>
                </tr>
            </thead>
            <tbody>
                @foreach (var order in _orders.OrderByDescending(o => o.CreatedAt))
                {
                    <tr>
                        <td class="order-id">
                            @(string.IsNullOrEmpty(order.Nickname)
                                ? order.Id[..Math.Min(8, order.Id.Length)]
                                : order.Nickname[..Math.Min(8, order.Nickname.Length)])
                        </td>
                        <td><span class="status-badge status-@order.Status">@order.Status</span></td>
                        <td>@order.Items.Sum(i => i.Quantity) pizza(s)</td>
                        <td>$@order.TotalPrice.ToString("F2")</td>
                        <td>@FormatTime(order.CreatedAt)</td>
                        <td>@FormatTime(order.EstimatedCompletionAt)</td>
                    </tr>
                }
            </tbody>
        </table>
    }
</div>

@code {
    private List<OrderResponse> _orders = [];
    private StatusResponse? _status;
    private string? _error;
    private DateTime _lastUpdated = DateTime.Now;
    private Timer? _timer;

    protected override async Task OnInitializedAsync()
    {
        await LoadDataAsync();
    }

    protected override void OnAfterRender(bool firstRender)
    {
        if (firstRender)
        {
            // The SignalR circuit isn't established until after first render, so starting
            // the timer here (rather than OnInitializedAsync) ensures the circuit is live
            // before the timer fires and StateHasChanged is called.
            _timer = new Timer(async _ =>
            {
                await LoadDataAsync();
                // Timer callbacks run on a thread-pool thread. Blazor requires UI updates
                // on its synchronization context — InvokeAsync marshals the call correctly.
                await InvokeAsync(StateHasChanged);
            }, null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
        }
    }

    private async Task LoadDataAsync()
    {
        try
        {
            _orders = await OrdersService.GetOrdersAsync();
            _status = await OrdersService.GetStatusAsync();
            _error = null; // clears the error banner automatically when the API comes back
        }
        catch (Exception ex)
        {
            _error = ex.Message;
        }
        _lastUpdated = DateTime.Now; // always update, even on error, so the user has timing context
    }

    private static string FormatTime(string? isoTime)
    {
        if (string.IsNullOrEmpty(isoTime)) return "-";
        return DateTime.TryParse(isoTime, out var dt) ? dt.ToLocalTime().ToString("HH:mm:ss") : isoTime;
    }

    public void Dispose() => _timer?.Dispose();
}
```

### Key concepts

**`@rendermode InteractiveServer`** — without this attribute, Blazor renders the component as static HTML. No SignalR connection, no event handling, no `StateHasChanged`. The attribute enables the interactive circuit for this component.

**`@implements IDisposable`** — Blazor calls `Dispose()` when the component is removed from the render tree (user navigates away, circuit closes). Without it, the timer keeps firing against a dead component.

**`OnInitializedAsync` vs `OnAfterRender(firstRender)`** — the initial data load happens in `OnInitializedAsync` because it runs during server-side prerendering. The timer starts in `OnAfterRender(firstRender: true)` because the SignalR circuit isn't established until the component is live in the browser — calling `InvokeAsync` before then would fail silently.

**`InvokeAsync(StateHasChanged)`** — `System.Threading.Timer` fires its callback on the .NET thread pool. Blazor's renderer expects to be called from its own synchronization context. `InvokeAsync` queues the delegate on the correct context, preventing subtle concurrency bugs.

---

## 6. Add CSS Isolation — `Components/Pages/Home.razor.css`

Create `Components/Pages/Home.razor.css` alongside `Home.razor`. Blazor automatically scopes styles in a `.razor.css` file to the component that shares its name — the compiler generates a unique attribute (like `b-xyz123`) and rewrites every selector to include it. No naming conflicts with global styles, no leakage.

```css
.dashboard {
    max-width: 1100px;
    margin: 2rem auto;
    padding: 0 1rem;
    font-family: system-ui, sans-serif;
}

.dashboard h1 {
    font-size: 1.8rem;
    margin-bottom: 1rem;
}

.stats {
    display: flex;
    gap: 2rem;
    margin-bottom: 0.5rem;
}

.stat {
    font-size: 1rem;
    color: #555;
}

.stat strong {
    color: #111;
}

.refresh-info {
    font-size: 0.8rem;
    color: #888;
    margin-bottom: 1.5rem;
}

.orders-table {
    width: 100%;
    border-collapse: collapse;
    font-size: 0.9rem;
}

.orders-table th,
.orders-table td {
    padding: 0.6rem 0.8rem;
    text-align: left;
    border-bottom: 1px solid #e5e7eb;
}

.orders-table th {
    background: #f9fafb;
    font-weight: 600;
    color: #374151;
}

.orders-table tr:hover {
    background: #f9fafb;
}

.order-id {
    font-family: monospace;
    font-size: 0.85rem;
    color: #6b7280;
}

.status-badge {
    display: inline-block;
    padding: 0.2rem 0.6rem;
    border-radius: 9999px;
    font-size: 0.75rem;
    font-weight: 600;
    text-transform: capitalize;
}

.status-pending {
    background: #fef3c7;
    color: #92400e;
}

.status-preparing {
    background: #dbeafe;
    color: #1e40af;
}

.status-ready {
    background: #d1fae5;
    color: #065f46;
}

.status-delivered {
    background: #e5e7eb;
    color: #374151;
}

.status-cancelled {
    background: #fee2e2;
    color: #991b1b;
}

.empty-state {
    text-align: center;
    padding: 3rem;
    color: #9ca3af;
}

.error-banner {
    background: #fee2e2;
    color: #991b1b;
    padding: 0.75rem 1rem;
    border-radius: 0.5rem;
    margin-bottom: 1rem;
    font-size: 0.9rem;
}
```

---

## 7. Set the Port in `launchSettings.json`

Update `Properties/launchSettings.json` to use port 4280 and skip opening a browser on launch:

```json
{
  "profiles": {
    "http": {
      "commandName": "Project",
      "launchBrowser": false,
      "applicationUrl": "http://localhost:4280",
      "environmentVariables": {
        "ASPNETCORE_ENVIRONMENT": "Development"
      }
    }
  }
}
```

---

## 8. Run It

```
dotnet run
```

Open `http://localhost:4280`. You should see the dashboard with an empty state message if no orders exist.

Place an order via the MCP server (Chapter 5) or directly against the API, then watch it appear in the table. Refresh happens automatically every 10 seconds — leave the tab open and the status badges will update as orders progress through `pending` → `preparing` → `ready` → `delivered`.

Add to the solution:

```
dotnet sln ../PizzaOrdering.slnx add PizzaBot.Dashboard/PizzaBot.Dashboard.csproj
```
