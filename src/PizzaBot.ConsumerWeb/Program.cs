using PizzaBot.ConsumerWeb.Components;
using PizzaBot.ConsumerWeb.Models;
using PizzaBot.ConsumerWeb.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// PizzaBot Foundry agent config
builder.Services.Configure<PizzaBotSettings>(builder.Configuration.GetSection("PizzaBot"));

// Voice Live settings + session factory
builder.Services.Configure<VoiceLiveSettings>(builder.Configuration.GetSection("VoiceLive"));
builder.Services.AddSingleton<VoiceLiveSessionService>();

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
app.UseWebSockets();
app.UseAntiforgery();

app.MapStaticAssets();

// WebSocket endpoint for Voice Live — browser audio in, events out
app.Map("/ws/voice-live", async (HttpContext context,
    VoiceLiveSessionService sessionService,
    ILogger<Program> logger,
    IHostApplicationLifetime lifetime) =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }

    var ws = await context.WebSockets.AcceptWebSocketAsync();
    await VoiceLiveWebSocketHandler.HandleAsync(ws, sessionService, logger, lifetime.ApplicationStopping);
});

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

