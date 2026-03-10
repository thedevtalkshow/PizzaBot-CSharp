namespace Shared.Models;

public enum OrderStatus
{
    Pending,
    InPreparation,
    Ready,
    Completed,
    Cancelled
}

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

public class ErrorResponse
{
    public string Error { get; set; } = "";
}

public class StatusResponse
{
    public string Status { get; set; } = "up";
    public int ActiveOrders { get; set; }
    public int TotalOrders { get; set; }
    public int RegisteredUsers { get; set; }
    public string Timestamp { get; set; } = "";
}
