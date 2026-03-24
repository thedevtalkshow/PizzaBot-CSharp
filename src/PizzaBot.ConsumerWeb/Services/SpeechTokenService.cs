using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Options;
using PizzaBot.ConsumerWeb.Models;

namespace PizzaBot.ConsumerWeb.Services;

public class SpeechTokenService(HttpClient httpClient, IOptions<AvatarSettings> avatarSettings)
{
    private readonly AvatarSettings _settings = avatarSettings.Value;

    public async Task RefreshSpeechTokenAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (!string.IsNullOrEmpty(_settings.SpeechPrivateEndpoint))
            {
                var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
                {
                    ManagedIdentityClientId = _settings.UserAssignedManagedIdentityClientId
                });
                var token = await credential.GetTokenAsync(
                    new TokenRequestContext(["https://cognitiveservices.azure.com/.default"]),
                    stoppingToken);
                GlobalAvatarVariables.SpeechToken = $"aad#{_settings.SpeechResourceUrl}#{token.Token}";
            }
            else
            {
                var url = $"https://{_settings.SpeechRegion}.api.cognitive.microsoft.com/sts/v1.0/issueToken";
                var request = new HttpRequestMessage(HttpMethod.Post, url);
                request.Headers.Add("Ocp-Apim-Subscription-Key", _settings.SpeechKey);

                var response = await httpClient.SendAsync(request, stoppingToken);
                response.EnsureSuccessStatusCode();

                GlobalAvatarVariables.SpeechToken = await response.Content.ReadAsStringAsync(stoppingToken);
            }

            Console.WriteLine($"[Speech] Token refreshed, prefix: {GlobalAvatarVariables.SpeechToken?[..Math.Min(10, GlobalAvatarVariables.SpeechToken?.Length ?? 0)]}");
            await Task.Delay(TimeSpan.FromMinutes(9), stoppingToken);
        }
    }
}
