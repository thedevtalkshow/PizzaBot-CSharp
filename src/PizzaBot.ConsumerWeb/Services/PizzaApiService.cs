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

    public async Task<List<OrderResponse>> GetOrdersAsync(string? userId = null, string? status = null)
    {
        var url = "/api/orders";
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(userId)) query.Add($"userId={Uri.EscapeDataString(userId)}");
        if (!string.IsNullOrWhiteSpace(status)) query.Add($"status={Uri.EscapeDataString(status)}");
        if (query.Count > 0) url += "?" + string.Join("&", query);
        return await httpClient.GetFromJsonAsync<List<OrderResponse>>(url, JsonOptions) ?? [];
    }
}
