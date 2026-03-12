using McpServer.Tools;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

var builder = WebApplication.CreateBuilder(args);

var pizzaApiUrl = builder.Configuration["PizzaApi:BaseUrl"] ?? "http://localhost:7071";

builder.Services.AddHttpClient("PizzaApi", client =>
{
    client.BaseAddress = new Uri(pizzaApiUrl);
    client.DefaultRequestHeaders.Add("Accept", "application/json");
});

// Configure all logs to go to stderr (stdout is used for the MCP protocol messages).
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

// Add the MCP services: the transport to use (stdio) and the tools to register.
builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithTools<PizzaTools>();

var app = builder.Build();

app.MapMcp("/mcp");

app.Run();

