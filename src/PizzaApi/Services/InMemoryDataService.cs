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
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        string? dataPath = null;
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "data");
            if (Directory.Exists(candidate))
            {
                dataPath = candidate;
                break;
            }
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
            var json = File.ReadAllText(pizzasFile);
            var pizzas = JsonSerializer.Deserialize<List<Pizza>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (pizzas != null) _pizzas.AddRange(pizzas);
        }
        else
        {
            _logger.LogWarning("pizzas.json not found in '{DataPath}'. Pizza menu will be empty.", dataPath);
        }

        if (File.Exists(toppingsFile))
        {
            var json = File.ReadAllText(toppingsFile);
            var toppings = JsonSerializer.Deserialize<List<Topping>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (toppings != null) _toppings.AddRange(toppings);
        }
        else
        {
            _logger.LogWarning("toppings.json not found in '{DataPath}'. Toppings menu will be empty.", dataPath);
        }

        if (_pizzas.Count == 0 && _toppings.Count == 0)
            _logger.LogWarning("No pizza or topping data was loaded. The menu will be empty.");
        else
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
    {
        var categories = _toppings.Select(t => t.Category).Distinct().OrderBy(c => c).ToList();
        return Task.FromResult(categories);
    }
    
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
        lock (_lock)
        {
            return Task.FromResult(_orders.FirstOrDefault(o => o.Id == id));
        }
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

    public Task<bool> UserExistsAsync(string userId) => Task.FromResult(true);

    public Task<int> GetRegisteredUsersCountAsync() => Task.FromResult(0);
}
