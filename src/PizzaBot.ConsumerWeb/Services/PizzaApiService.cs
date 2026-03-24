using Shared.Models;
using System.Text.Json;

namespace PizzaBot.ConsumerWeb.Services;

/// <summary>
/// HTTP client wrapper for the PizzaApi — used by the cart panel to show live orders.
/// </summary>
public class PizzaApiService(HttpClient httpClient)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<List<Pizza>> GetPizzasAsync() =>
        await httpClient.GetFromJsonAsync<List<Pizza>>("/api/pizzas", JsonOptions) ?? [];

    public async Task<Pizza?> GetPizzaAsync(string id) =>
        await httpClient.GetFromJsonAsync<Pizza?>($"/api/pizzas/{id}", JsonOptions);

    public async Task<List<OrderResponse>> GetOrdersAsync(string userId, string? status = null)
    {
        var url = $"/api/orders?userId={Uri.EscapeDataString(userId)}";
        if (!string.IsNullOrEmpty(status)) url += $"&status={Uri.EscapeDataString(status)}";
        return await httpClient.GetFromJsonAsync<List<OrderResponse>>(url, JsonOptions) ?? [];
    }
}
