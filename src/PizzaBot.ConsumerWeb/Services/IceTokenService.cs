using Microsoft.Extensions.Options;
using PizzaBot.ConsumerWeb.Models;

namespace PizzaBot.ConsumerWeb.Services;

public class IceTokenService(HttpClient httpClient, IOptions<AvatarSettings> avatarSettings)
{
    private readonly AvatarSettings _settings = avatarSettings.Value;

    public async Task RefreshIceTokenAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var url = !string.IsNullOrEmpty(_settings.SpeechPrivateEndpoint)
                ? $"{_settings.SpeechPrivateEndpoint.TrimEnd('/')}/tts/cognitiveservices/avatar/relay/token/v1"
                : $"https://{_settings.SpeechRegion}.tts.speech.microsoft.com/cognitiveservices/avatar/relay/token/v1";

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Ocp-Apim-Subscription-Key", _settings.SpeechKey);

            var response = await httpClient.SendAsync(request, stoppingToken);
            response.EnsureSuccessStatusCode();

            GlobalAvatarVariables.IceToken = await response.Content.ReadAsStringAsync(stoppingToken);
            Console.WriteLine($"[ICE] Token refreshed.");

            await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
        }
    }
}
