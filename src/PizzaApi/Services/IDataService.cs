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
