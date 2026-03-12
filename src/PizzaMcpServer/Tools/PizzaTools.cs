using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Net.Http.Json;

namespace PizzaMcpServer.Tools
{
    [McpServerToolType]
    internal class PizzaTools(IHttpClientFactory httpClientFactory)
    {
        private HttpClient Client => httpClientFactory.CreateClient("PizzaApi");

        private async Task<string> FetchAsync(string url, HttpMethod? method = null, object? body = null)
        {
            var request = new HttpRequestMessage(method ?? HttpMethod.Get, url);
            if (body != null)
            {
                request.Content = JsonContent.Create(body);
            }
            var response = await Client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
                throw new Exception($"API error {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
            if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
                return "Operation completed successfully. No content returned.";
            return await response.Content.ReadAsStringAsync();
        }


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

public record PlaceOrderItem(
    [property: Description("ID of the pizza")] string PizzaId,
    [property: Description("Quantity of the pizza")] int Quantity,
    [property: Description("List of extra topping IDs")] string[]? ExtraToppingIds = null
);

