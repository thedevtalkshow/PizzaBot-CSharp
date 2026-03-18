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
