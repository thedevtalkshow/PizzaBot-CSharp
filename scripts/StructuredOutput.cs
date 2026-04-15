#:package Azure.AI.Projects@2.0.0
#:package Microsoft.Agents.AI.Foundry@1.0.0
#:package Azure.Identity@1.20.0

using Azure.AI.Projects;
using Microsoft.Agents.AI;
using Azure.Identity;
using System.Text.Json.Serialization;
using System.Text.Json;

var endpoint = "https://structuredoutput-foundry.services.ai.azure.com/api/projects/structuredoutput";
var deployment = "gpt-4.1-mini";

AIAgent agent = new AIProjectClient(new Uri(endpoint), new DefaultAzureCredential())
    .AsAIAgent(
        model: deployment,
        instructions: "You are a friendly assistant. Keep your answers brief.",
        name: "HelloAgent");

// Create JsonSerializerOptions with the custom type info
var jsonOptions = new JsonSerializerOptions
{
    TypeInfoResolver = StructuredOutputJsonContext.Default
};

AgentResponse<CityInfo> response = await agent.RunAsync<CityInfo>("Please provide information about the capital of California.", serializerOptions: jsonOptions);

Console.WriteLine($"Name: {response.Result.Name}, Population: {response.Result.Population}, Capital: {response.Result.Capital}, Year Founded: {response.Result.YearFounded}");        

public class CityInfo
{
    public string? Name { get; set; }
    public int? Population { get; set; }
    public string? Capital { get; set; }
    public int? YearFounded { get; set; }
}

[JsonSerializable(typeof(CityInfo))]
public partial class StructuredOutputJsonContext : JsonSerializerContext
{
}