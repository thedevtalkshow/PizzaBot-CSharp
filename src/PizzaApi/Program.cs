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
app.UseStaticFiles();

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
app.MapGet("/api/pizzas", async (HttpContext http, IDataService db) =>
{
    var baseUrl = $"{http.Request.Scheme}://{http.Request.Host}";
    var pizzas = (await db.GetPizzasAsync()).Select(p => WithAbsoluteImageUrl(p, baseUrl));
    return Results.Json(pizzas, jsonOptions);
});

app.MapGet("/api/pizzas/{id}", async (string id, HttpContext http, IDataService db) =>
{
    var pizza = await db.GetPizzaAsync(id);
    if (pizza is null)
        return Results.Json(new ErrorResponse { Error = $"Pizza with ID {id} not found" }, jsonOptions, statusCode: 404);

    var baseUrl = $"{http.Request.Scheme}://{http.Request.Host}";
    return Results.Json(WithAbsoluteImageUrl(pizza, baseUrl), jsonOptions);
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

    var totalCount = request.Items.Sum(i => i.Quantity);
    if (totalCount > 50)
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

// Orders - DELETE
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

// ── Helpers ──────────────────────────────────────────────────────────────────

// Transform a bare image filename (e.g. "pizza-pic-1.jpg") to a full URL
// pointing at the static file served by this API.
static Pizza WithAbsoluteImageUrl(Pizza pizza, string baseUrl) =>
    string.IsNullOrEmpty(pizza.ImageUrl) ? pizza
    : new Pizza
    {
        Id = pizza.Id,
        Name = pizza.Name,
        Description = pizza.Description,
        Price = pizza.Price,
        Toppings = pizza.Toppings,
        ImageUrl = $"{baseUrl}/images/{pizza.ImageUrl}",
    };

app.Run();
