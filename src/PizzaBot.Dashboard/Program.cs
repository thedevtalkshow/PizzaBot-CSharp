using PizzaBot.Dashboard.Components;
using PizzaBot.Dashboard.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var pizzaApiUrl = builder.Configuration["PizzaApi:BaseUrl"] ?? "http://localhost:7071";
builder.Services.AddHttpClient<OrdersService>(client =>
{
    client.BaseAddress = new Uri(pizzaApiUrl);
});

var app = builder.Build();

app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
