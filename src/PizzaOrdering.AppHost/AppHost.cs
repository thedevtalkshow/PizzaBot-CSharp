var builder = DistributedApplication.CreateBuilder(args);

var api = builder.AddProject<Projects.PizzaApi>("pizzaAPI")
                .WithExternalHttpEndpoints();

var mcp = builder.AddProject<Projects.PizzaMcpServer>("pizzaMCP")
                .WithExternalHttpEndpoints()
                .WithReference(api)
                .WaitFor(api);

var dashboard = builder.AddProject<Projects.PizzaBot_Dashboard>("pizzaDash")
                .WithExternalHttpEndpoints()
                .WithReference(api)
                .WaitFor(api);

var consumerWeb = builder.AddProject<Projects.PizzaBot_ConsumerWeb>("pizzaConsumerWeb")
                .WithExternalHttpEndpoints()
                .WithReference(api)
                .WaitFor(api);

builder.Build().Run();
