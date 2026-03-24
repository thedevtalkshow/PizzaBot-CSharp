namespace PizzaBot.ConsumerWeb.Services;

public class TokenRefreshBackgroundService(IceTokenService iceTokenService, SpeechTokenService speechTokenService)
    : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.WhenAll(
            iceTokenService.RefreshIceTokenAsync(stoppingToken),
            speechTokenService.RefreshSpeechTokenAsync(stoppingToken));
}
