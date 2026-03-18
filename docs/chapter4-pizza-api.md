# Chapter 4 — Building the Pizza API

The workshop samples connect to a hosted Contoso Pizza MCP server by default. This chapter builds the local backend that server proxies: a full ASP.NET Core Minimal API with pizza menu and ordering endpoints, an in-memory data service, and a background worker that advances order status automatically.

After this chapter you can run the entire stack locally — API, MCP server, and agents — without depending on any hosted workshop services.

## What you'll build

- **`Shared`** — a class library with the shared domain models used by every project
- **`PizzaApi`** — an ASP.NET Core Minimal API serving pizza, topping, and order endpoints
- An in-memory `IDataService` that loads menu data from JSON files at startup
- An `OrderStatusBackgroundService` that simulates orders progressing through their lifecycle

## 1. Create the Shared library

The `PizzaApi` and `PizzaMcpServer` projects both depend on the domain models, so Shared gets created first.

```bash
dotnet new classlib -n Shared
cd Shared
```

Delete the default `Class1.cs` and create a `Models/` folder. You'll add three files.

### Models/Pizza.cs

```csharp
namespace Shared.Models;

public class Pizza
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal Price { get; set; }
    public string ImageUrl { get; set; } = "";
    public List<string> Toppings { get; set; } = []; // IDs of default toppings
}
```

### Models/Topping.cs

```csharp
namespace Shared.Models;

public class Topping
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal Price { get; set; }
    public string ImageUrl { get; set; } = "";
    public string Category { get; set; } = "";
}
```

### Models/Order.cs

This file is more substantial — it contains the full order domain. The key design point: `OrderResponse` is the public DTO exposed by the API and never includes `UserId`. `Order` is the internal domain model that carries it for ownership checks. The MCP server and dashboard only ever see `OrderResponse`.

```csharp
namespace Shared.Models;

public enum OrderStatus { Pending, InPreparation, Ready, Completed, Cancelled }

public static class OrderStatusStrings
{
    public const string Pending = "pending";
    public const string InPreparation = "in-preparation";
    public const string Ready = "ready";
    public const string Completed = "completed";
    public const string Cancelled = "cancelled";

    public static string FromEnum(OrderStatus status) => status switch
    {
        OrderStatus.Pending => Pending,
        OrderStatus.InPreparation => InPreparation,
        OrderStatus.Ready => Ready,
        OrderStatus.Completed => Completed,
        OrderStatus.Cancelled => Cancelled,
        _ => Pending
    };

    public static OrderStatus ToEnum(string status) => status switch
    {
        Pending => OrderStatus.Pending,
        InPreparation => OrderStatus.InPreparation,
        Ready => OrderStatus.Ready,
        Completed => OrderStatus.Completed,
        Cancelled => OrderStatus.Cancelled,
        _ => OrderStatus.Pending
    };
}

public class OrderItem
{
    public string PizzaId { get; set; } = "";
    public int Quantity { get; set; }
    public List<string>? ExtraToppingIds { get; set; }
}

public class Order
{
    public string Id { get; set; } = "";
    public string UserId { get; set; } = "";
    public string? Nickname { get; set; }
    public string CreatedAt { get; set; } = "";
    public List<OrderItem> Items { get; set; } = [];
    public string EstimatedCompletionAt { get; set; } = "";
    public decimal TotalPrice { get; set; }
    public string Status { get; set; } = OrderStatusStrings.Pending;
    public string? ReadyAt { get; set; }
    public string? CompletedAt { get; set; }
}

public class OrderResponse
{
    public string Id { get; set; } = "";
    public string? Nickname { get; set; }
    public string CreatedAt { get; set; } = "";
    public List<OrderItem> Items { get; set; } = [];
    public string EstimatedCompletionAt { get; set; } = "";
    public decimal TotalPrice { get; set; }
    public string Status { get; set; } = OrderStatusStrings.Pending;
    public string? ReadyAt { get; set; }
    public string? CompletedAt { get; set; }

    public static OrderResponse FromOrder(Order order) => new()
    {
        Id = order.Id,
        Nickname = order.Nickname,
        CreatedAt = order.CreatedAt,
        Items = order.Items,
        EstimatedCompletionAt = order.EstimatedCompletionAt,
        TotalPrice = order.TotalPrice,
        Status = order.Status,
        ReadyAt = order.ReadyAt,
        CompletedAt = order.CompletedAt
    };
}

public class CreateOrderRequest
{
    public string UserId { get; set; } = "";
    public string? Nickname { get; set; }
    public List<CreateOrderItem> Items { get; set; } = [];
}

public class CreateOrderItem
{
    public string PizzaId { get; set; } = "";
    public int Quantity { get; set; }
    public List<string>? ExtraToppingIds { get; set; }
}

public class ErrorResponse { public string Error { get; set; } = ""; }

public class StatusResponse
{
    public string Status { get; set; } = "up";
    public int ActiveOrders { get; set; }
    public int TotalOrders { get; set; }
    public int RegisteredUsers { get; set; }
    public string Timestamp { get; set; } = "";
}
```

## 2. Create the PizzaApi project

```bash
cd ..
dotnet new webapi --use-minimal-api -n PizzaApi
cd PizzaApi
```

Add a project reference to Shared:

```bash
dotnet add reference ../Shared/Shared.csproj
```

Delete the sample WeatherForecast code from `Program.cs` — you'll replace it entirely.

### launchSettings.json

The API runs on port 7071 by default. Update `Properties/launchSettings.json` to set a consistent port and skip the browser launch:

```json
{
  "$schema": "https://json.schemastore.org/launchsettings.json",
  "profiles": {
    "http": {
      "commandName": "Project",
      "dotnetRunMessages": true,
      "launchBrowser": false,
      "applicationUrl": "http://localhost:7071",
      "environmentVariables": {
        "ASPNETCORE_ENVIRONMENT": "Development"
      }
    }
  }
}
```

## 3. IDataService

Create `Services/IDataService.cs`. The interface is what gets registered in DI — all endpoints depend on it rather than the concrete implementation. This means you can swap `InMemoryDataService` for a Cosmos DB implementation without touching `Program.cs` or any endpoint logic.

```csharp
using Shared.Models;

namespace PizzaApi.Services;

public interface IDataService
{
    Task<List<Pizza>> GetPizzasAsync();
    Task<Pizza?> GetPizzaAsync(string id);
    Task<List<Topping>> GetToppingsAsync(string? category = null);
    Task<List<string>> GetToppingCategoriesAsync();
    Task<Topping?> GetToppingAsync(string id);
    Task<List<Order>> GetOrdersAsync(string? userId = null, string[]? statuses = null, TimeSpan? last = null);
    Task<Order?> GetOrderAsync(string id);
    Task<Order> CreateOrderAsync(Order order);
    Task<Order?> CancelOrderAsync(string id);
    Task<Order?> UpdateOrderAsync(string id, Action<Order> update);
    Task<bool> UserExistsAsync(string userId);
    Task<int> GetRegisteredUsersCountAsync();
}
```

## 4. InMemoryDataService

Create `Services/InMemoryDataService.cs`. Three things worth explaining before the code:

**Data loading**: `LoadData()` walks up the directory tree from `AppContext.BaseDirectory` looking for a `data/` folder. This handles the mismatch between the deep debug output path (`bin/Debug/net10.0/`) and a published flat output — without it, a hardcoded relative path would break depending on how you run the app. The `data/` folder lives at the solution root.

**Locking**: All order mutations (`CreateOrderAsync`, `CancelOrderAsync`, `UpdateOrderAsync`) use `lock (_lock)`. Reads on orders also lock because the background service mutates them concurrently. Pizza and topping lists are populated once at startup and never mutated, so they don't need locking.

**UserExistsAsync**: Returns `true` for all user IDs. This is intentional for the workshop — you can place orders without a separate user registration step. In production you'd validate against an identity store.

```csharp
using System.Text.Json;
using Shared.Models;

namespace PizzaApi.Services;

public class InMemoryDataService : IDataService
{
    private readonly List<Pizza> _pizzas = [];
    private readonly List<Topping> _toppings = [];
    private readonly List<Order> _orders = [];
    private readonly object _lock = new();
    private readonly ILogger<InMemoryDataService> _logger;

    public InMemoryDataService(ILogger<InMemoryDataService> logger)
    {
        _logger = logger;
        LoadData();
    }

    private void LoadData()
    {
        // Walk up from the output directory until we find a 'data' folder.
        // This works whether running from bin/Debug/net10.0 or a published output.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        string? dataPath = null;
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "data");
            if (Directory.Exists(candidate)) { dataPath = candidate; break; }
            dir = dir.Parent;
        }

        if (dataPath == null)
        {
            _logger.LogWarning("No 'data' directory found. Pizza and topping menus will be empty. " +
                               "Add a 'data' folder with pizzas.json and toppings.json to the solution root.");
            return;
        }

        var pizzasFile = Path.Combine(dataPath, "pizzas.json");
        var toppingsFile = Path.Combine(dataPath, "toppings.json");

        if (File.Exists(pizzasFile))
        {
            var pizzas = JsonSerializer.Deserialize<List<Pizza>>(
                File.ReadAllText(pizzasFile),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (pizzas != null) _pizzas.AddRange(pizzas);
        }
        else
            _logger.LogWarning("pizzas.json not found in '{DataPath}'. Pizza menu will be empty.", dataPath);

        if (File.Exists(toppingsFile))
        {
            var toppings = JsonSerializer.Deserialize<List<Topping>>(
                File.ReadAllText(toppingsFile),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (toppings != null) _toppings.AddRange(toppings);
        }
        else
            _logger.LogWarning("toppings.json not found in '{DataPath}'. Toppings menu will be empty.", dataPath);

        _logger.LogInformation("Loaded {PizzaCount} pizzas and {ToppingCount} toppings.", _pizzas.Count, _toppings.Count);
    }

    public Task<List<Pizza>> GetPizzasAsync() => Task.FromResult(_pizzas.ToList());
    public Task<Pizza?> GetPizzaAsync(string id) => Task.FromResult(_pizzas.FirstOrDefault(p => p.Id == id));

    public Task<List<Topping>> GetToppingsAsync(string? category = null)
    {
        var toppings = string.IsNullOrEmpty(category)
            ? _toppings.ToList()
            : _toppings.Where(t => t.Category.Equals(category, StringComparison.OrdinalIgnoreCase)).ToList();
        return Task.FromResult(toppings);
    }

    public Task<List<string>> GetToppingCategoriesAsync()
        => Task.FromResult(_toppings.Select(t => t.Category).Distinct().OrderBy(c => c).ToList());

    public Task<Topping?> GetToppingAsync(string id) => Task.FromResult(_toppings.FirstOrDefault(t => t.Id == id));

    public Task<List<Order>> GetOrdersAsync(string? userId = null, string[]? statuses = null, TimeSpan? last = null)
    {
        lock (_lock)
        {
            var orders = _orders.AsEnumerable();
            if (!string.IsNullOrEmpty(userId))
                orders = orders.Where(o => o.UserId == userId);
            if (statuses is { Length: > 0 })
                orders = orders.Where(o => statuses.Contains(o.Status, StringComparer.OrdinalIgnoreCase));
            if (last.HasValue)
            {
                var since = DateTime.UtcNow - last.Value;
                orders = orders.Where(o => DateTime.TryParse(o.CreatedAt, out var dt) && dt >= since);
            }
            return Task.FromResult(orders.ToList());
        }
    }

    public Task<Order?> GetOrderAsync(string id)
    {
        lock (_lock) { return Task.FromResult(_orders.FirstOrDefault(o => o.Id == id)); }
    }

    public Task<Order> CreateOrderAsync(Order order)
    {
        lock (_lock)
        {
            order.Id = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
            _orders.Add(order);
            return Task.FromResult(order);
        }
    }

    public Task<Order?> CancelOrderAsync(string id)
    {
        lock (_lock)
        {
            var order = _orders.FirstOrDefault(o => o.Id == id);
            // Only pending orders can be cancelled
            if (order == null || order.Status != OrderStatusStrings.Pending)
                return Task.FromResult<Order?>(null);
            order.Status = OrderStatusStrings.Cancelled;
            return Task.FromResult<Order?>(order);
        }
    }

    public Task<Order?> UpdateOrderAsync(string id, Action<Order> update)
    {
        lock (_lock)
        {
            var order = _orders.FirstOrDefault(o => o.Id == id);
            if (order == null) return Task.FromResult<Order?>(null);
            update(order);
            return Task.FromResult<Order?>(order);
        }
    }

    // Accepts any userId — in production you'd validate against an identity store
    public Task<bool> UserExistsAsync(string userId) => Task.FromResult(true);
    public Task<int> GetRegisteredUsersCountAsync() => Task.FromResult(0);
}
```

## 5. OrderStatusBackgroundService

Create `Services/OrderStatusBackgroundService.cs`. This service runs every 40 seconds and advances orders through their lifecycle: `pending → in-preparation → ready → completed`.

The timing is probabilistic rather than deterministic: a pending order can move to in-preparation after 1 minute (50% chance) or always after 3 minutes. This simulates a real kitchen without needing a timer per order.

Note the `IServiceProvider` scope pattern: even though `IDataService` is registered as a singleton (so scoping isn't strictly required here), using `CreateScope()` is the correct pattern. It means this service works correctly if you later change `IDataService` to a scoped registration such as an EF Core `DbContext`.

```csharp
using Shared.Models;

namespace PizzaApi.Services;

public class OrderStatusBackgroundService(IServiceProvider services, ILogger<OrderStatusBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(40), stoppingToken);
            try
            {
                using var scope = services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<IDataService>();
                await UpdateOrderStatuses(db);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error updating order statuses");
            }
        }
    }

    private async Task UpdateOrderStatuses(IDataService db)
    {
        var now = DateTime.UtcNow;
        var rng = Random.Shared;
        var activeOrders = await db.GetOrdersAsync(statuses: [
            OrderStatusStrings.Pending,
            OrderStatusStrings.InPreparation,
            OrderStatusStrings.Ready
        ]);

        foreach (var order in activeOrders)
        {
            if (order.Status == OrderStatusStrings.Pending)
            {
                if (!DateTime.TryParse(order.CreatedAt, out var createdAt)) continue;
                var minutesSinceCreated = (now - createdAt).TotalMinutes;
                if (minutesSinceCreated > 3 || (minutesSinceCreated >= 1 && rng.NextDouble() < 0.5))
                {
                    await db.UpdateOrderAsync(order.Id, o => o.Status = OrderStatusStrings.InPreparation);
                    logger.LogInformation("Order {Id} → in-preparation", order.Id);
                }
            }
            else if (order.Status == OrderStatusStrings.InPreparation)
            {
                if (!DateTime.TryParse(order.EstimatedCompletionAt, out var eta)) continue;
                var diffMinutes = (now - eta).TotalMinutes;
                if (diffMinutes > 3 || (Math.Abs(diffMinutes) <= 3 && rng.NextDouble() < 0.5))
                {
                    await db.UpdateOrderAsync(order.Id, o =>
                    {
                        o.Status = OrderStatusStrings.Ready;
                        o.ReadyAt = now.ToString("O");
                    });
                    logger.LogInformation("Order {Id} → ready", order.Id);
                }
            }
            else if (order.Status == OrderStatusStrings.Ready && order.ReadyAt != null)
            {
                if (!DateTime.TryParse(order.ReadyAt, out var readyAt)) continue;
                var minutesSinceReady = (now - readyAt).TotalMinutes;
                if (minutesSinceReady >= 1 && (minutesSinceReady > 2 || rng.NextDouble() < 0.5))
                {
                    await db.UpdateOrderAsync(order.Id, o =>
                    {
                        o.Status = OrderStatusStrings.Completed;
                        o.CompletedAt = now.ToString("O");
                    });
                    logger.LogInformation("Order {Id} → completed", order.Id);
                }
            }
        }
    }
}
```

## 6. Program.cs

Replace the generated `Program.cs` entirely. Key decisions:

- **CORS** with `AllowAnyOrigin/Method/Header` — the Blazor dashboard and any browser client need unrestricted access in a local dev scenario
- **Shared `JsonSerializerOptions`** with camelCase — passed explicitly to every `Results.Json(...)` call so serialization is consistent and doesn't depend on global middleware defaults
- **POST validation**: userId required, at least one item, positive quantities, valid pizza/topping IDs, max 50 pizzas per order, max 5 active orders per user

```csharp
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using PizzaApi.Services;
using Shared.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<IDataService, InMemoryDataService>();
builder.Services.AddHostedService<OrderStatusBackgroundService>();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

var app = builder.Build();
app.UseCors();

var jsonOptions = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    WriteIndented = false
};

// Status
app.MapGet("/api", async (IDataService db) =>
{
    var orders = await db.GetOrdersAsync();
    var activeOrders = orders.Count(o => o.Status is OrderStatusStrings.Pending or OrderStatusStrings.InPreparation or OrderStatusStrings.Ready);
    var registeredUsers = await db.GetRegisteredUsersCountAsync();
    return Results.Json(new StatusResponse
    {
        Status = "up",
        ActiveOrders = activeOrders,
        TotalOrders = orders.Count,
        RegisteredUsers = registeredUsers,
        Timestamp = DateTime.UtcNow.ToString("O")
    }, jsonOptions);
});

// Pizzas
app.MapGet("/api/pizzas", async (IDataService db) =>
    Results.Json(await db.GetPizzasAsync(), jsonOptions));

app.MapGet("/api/pizzas/{id}", async (string id, IDataService db) =>
{
    var pizza = await db.GetPizzaAsync(id);
    return pizza is null
        ? Results.Json(new ErrorResponse { Error = $"Pizza with ID {id} not found" }, jsonOptions, statusCode: 404)
        : Results.Json(pizza, jsonOptions);
});

// Toppings
app.MapGet("/api/toppings", async ([FromQuery] string? category, IDataService db) =>
    Results.Json(await db.GetToppingsAsync(category), jsonOptions));

app.MapGet("/api/toppings/categories", async (IDataService db) =>
    Results.Json(await db.GetToppingCategoriesAsync(), jsonOptions));

app.MapGet("/api/toppings/{id}", async (string id, IDataService db) =>
{
    var topping = await db.GetToppingAsync(id);
    return topping is null
        ? Results.Json(new ErrorResponse { Error = $"Topping with ID {id} not found" }, jsonOptions, statusCode: 404)
        : Results.Json(topping, jsonOptions);
});

// Orders - GET
app.MapGet("/api/orders", async ([FromQuery] string? userId, [FromQuery] string? status, [FromQuery] string? last, IDataService db) =>
{
    string[]? statuses = status?.Split(',', StringSplitOptions.RemoveEmptyEntries);
    TimeSpan? lastSpan = null;
    if (!string.IsNullOrEmpty(last))
    {
        if (last.EndsWith('h') && int.TryParse(last[..^1], out var h)) lastSpan = TimeSpan.FromHours(h);
        else if (last.EndsWith('m') && int.TryParse(last[..^1], out var m)) lastSpan = TimeSpan.FromMinutes(m);
    }
    var orders = await db.GetOrdersAsync(userId, statuses, lastSpan);
    return Results.Json(orders.Select(OrderResponse.FromOrder), jsonOptions);
});

app.MapGet("/api/orders/{id}", async (string id, IDataService db) =>
{
    var order = await db.GetOrderAsync(id);
    return order is null
        ? Results.Json(new ErrorResponse { Error = $"Order with ID {id} not found" }, jsonOptions, statusCode: 404)
        : Results.Json(OrderResponse.FromOrder(order), jsonOptions);
});

// Orders - POST
app.MapPost("/api/orders", async ([FromBody] CreateOrderRequest request, IDataService db) =>
{
    if (string.IsNullOrEmpty(request.UserId))
        return Results.Json(new ErrorResponse { Error = "userId is required" }, jsonOptions, statusCode: 400);

    var userExists = await db.UserExistsAsync(request.UserId);
    if (!userExists)
        return Results.Json(new ErrorResponse { Error = "The specified userId is not registered." }, jsonOptions, statusCode: 401);

    if (request.Items == null || request.Items.Count == 0)
        return Results.Json(new ErrorResponse { Error = "Order must contain at least one pizza" }, jsonOptions, statusCode: 400);

    if (request.Items.Sum(i => i.Quantity) > 50)
        return Results.Json(new ErrorResponse { Error = "Order cannot exceed 50 pizzas in total" }, jsonOptions, statusCode: 400);

    var activeOrders = await db.GetOrdersAsync(request.UserId, [OrderStatusStrings.Pending, OrderStatusStrings.InPreparation]);
    if (activeOrders.Count >= 5)
        return Results.Json(new ErrorResponse { Error = "Too many active orders: limit is 5 per user" }, jsonOptions, statusCode: 429);

    var orderItems = new List<OrderItem>();
    decimal totalPrice = 0;

    foreach (var item in request.Items)
    {
        if (item.Quantity <= 0)
            return Results.Json(new ErrorResponse { Error = $"Quantity for pizzaId {item.PizzaId} must be a positive integer" }, jsonOptions, statusCode: 400);

        var pizza = await db.GetPizzaAsync(item.PizzaId);
        if (pizza == null)
            return Results.Json(new ErrorResponse { Error = $"Pizza with ID {item.PizzaId} not found" }, jsonOptions, statusCode: 400);

        decimal extraPrice = 0;
        if (item.ExtraToppingIds?.Count > 0)
        {
            foreach (var toppingId in item.ExtraToppingIds)
            {
                var topping = await db.GetToppingAsync(toppingId);
                if (topping == null)
                    return Results.Json(new ErrorResponse { Error = $"Topping with ID {toppingId} not found" }, jsonOptions, statusCode: 400);
                extraPrice += topping.Price;
            }
        }

        totalPrice += (pizza.Price + extraPrice) * item.Quantity;
        orderItems.Add(new OrderItem { PizzaId = item.PizzaId, Quantity = item.Quantity, ExtraToppingIds = item.ExtraToppingIds });
    }

    var now = DateTime.UtcNow;
    var pizzaCount = orderItems.Sum(i => i.Quantity);
    // Estimated time scales with order size: base 3-5 min, +1 min per pizza beyond 2
    var minMinutes = 3;
    var maxMinutes = 5;
    if (pizzaCount > 2) { minMinutes += pizzaCount - 2; maxMinutes += pizzaCount - 2; }
    var estimatedMinutes = Random.Shared.Next(minMinutes, maxMinutes + 1);

    var order = new Order
    {
        UserId = request.UserId,
        Nickname = request.Nickname,
        CreatedAt = now.ToString("O"),
        Items = orderItems,
        EstimatedCompletionAt = now.AddMinutes(estimatedMinutes).ToString("O"),
        TotalPrice = totalPrice,
        Status = OrderStatusStrings.Pending
    };

    var created = await db.CreateOrderAsync(order);
    return Results.Json(OrderResponse.FromOrder(created), jsonOptions, statusCode: 201);
});

// Orders - DELETE (cancel)
app.MapDelete("/api/orders/{id}", async (string id, [FromQuery] string? userId, IDataService db) =>
{
    if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(userId))
        return Results.Json(new ErrorResponse { Error = "Order ID and userId are required" }, jsonOptions, statusCode: 400);

    var order = await db.GetOrderAsync(id);
    if (order == null)
        return Results.Json(new ErrorResponse { Error = $"Order with ID {id} not found" }, jsonOptions, statusCode: 404);

    if (order.UserId != userId)
        return Results.Json(new ErrorResponse { Error = "Not authorized to cancel this order" }, jsonOptions, statusCode: 403);

    var cancelled = await db.CancelOrderAsync(id);
    return cancelled is null
        ? Results.Json(new ErrorResponse { Error = $"Order {id} cannot be cancelled (not in pending status)" }, jsonOptions, statusCode: 404)
        : Results.Json(OrderResponse.FromOrder(cancelled), jsonOptions);
});

app.Run();
```

## 7. Data files

`pizzas.json` and `toppings.json` live in the `data/` folder at the solution root. They're committed to the repository and loaded once at startup by `InMemoryDataService.LoadData()`.

The API won't crash if they're missing — it starts cleanly and logs warnings. You can get the data files from the [original Python workshop repository](https://github.com/Azure-Samples/pizza-mcp-agents/tree/main/src/pizza-api/data) if you need to populate them:

```
pizzabot/
  data/
    pizzas.json
    toppings.json
```

## 8. Run it

```bash
dotnet run
```

The API starts on `http://localhost:7071`. Test with the included `PizzaApi.http` file or curl:

```bash
curl http://localhost:7071/api
curl http://localhost:7071/api/pizzas
curl http://localhost:7071/api/toppings/categories
```

The `/api` status endpoint returns active order counts and a timestamp — a quick sanity check that the service is up.

## 9. Add both projects to the solution

```bash
dotnet sln ../PizzaOrdering.slnx add Shared/Shared.csproj
dotnet sln ../PizzaOrdering.slnx add PizzaApi/PizzaApi.csproj
```

## What's next

With the API running locally you can point the MCP server at it instead of the hosted backend. See [local-stack.md](./local-stack.md) for the full local stack walkthrough including tunneling the MCP server so Azure AI Foundry can reach it.
