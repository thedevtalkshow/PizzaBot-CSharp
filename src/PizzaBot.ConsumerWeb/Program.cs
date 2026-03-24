using PizzaBot.ConsumerWeb.Components;
using PizzaBot.ConsumerWeb.Models;
using PizzaBot.ConsumerWeb.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddControllers();

// Avatar config
builder.Services.Configure<AvatarSettings>(builder.Configuration.GetSection("Avatar"));

// PizzaBot Foundry agent config
builder.Services.Configure<PizzaBotSettings>(builder.Configuration.GetSection("PizzaBot"));

// Avatar services
builder.Services.AddSingleton<IClientService, ClientService>();
builder.Services.AddSingleton<FoundryAgentService>();
builder.Services.AddHttpClient<IceTokenService>();
builder.Services.AddHttpClient<SpeechTokenService>();
builder.Services.AddHostedService<TokenRefreshBackgroundService>();

// Pizza API HTTP client
builder.Services.AddHttpClient<PizzaApiService>(client =>
{
    var baseUrl = builder.Configuration["PizzaApi:BaseUrl"] ?? "http://localhost:7071";
    client.BaseAddress = new Uri(baseUrl);
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapControllers();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

