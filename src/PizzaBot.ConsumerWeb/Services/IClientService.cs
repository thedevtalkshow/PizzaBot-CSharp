using PizzaBot.ConsumerWeb.Models;

namespace PizzaBot.ConsumerWeb.Services;

public interface IClientService
{
    Guid InitializeClient();
    AvatarClientContext GetClientContext(Guid clientId);
    void RemoveClient(Guid clientId);
}
