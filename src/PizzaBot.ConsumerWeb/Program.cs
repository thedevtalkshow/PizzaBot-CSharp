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

// Pizza API HTTP client — optional, used by Orders page when API is available
if (!string.IsNullOrEmpty(builder.Configuration["PizzaApi:BaseUrl"]))
{
    builder.Services.AddHttpClient<PizzaApiService>(client =>
    {
        client.BaseAddress = new Uri(builder.Configuration["PizzaApi:BaseUrl"]!);
    });
}
else
{
    // Register with a no-op base address so injection doesn't fail
    builder.Services.AddHttpClient<PizzaApiService>(client =>
    {
        client.BaseAddress = new Uri("http://localhost:7071");
    });
}

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

