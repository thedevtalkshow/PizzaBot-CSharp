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
