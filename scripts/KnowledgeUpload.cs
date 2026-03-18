#:package Azure.AI.Agents.Persistent@1.1.0
#:package Azure.AI.Projects@1.2.0-beta.5

#:property EnablePreviewFeatures=true

using Azure.AI.Agents.Persistent;
using Azure.AI.Projects;
using Azure.Identity;

// Usage: dotnet run KnowledgeUpload.cs <project-endpoint> [knowledge-folder]
// Example: dotnet run KnowledgeUpload.cs https://<resource>.services.ai.azure.com/api/projects/<project>

if (args.Length == 0)
{
    Console.WriteLine("Usage: dotnet run KnowledgeUpload.cs <project-endpoint> [knowledge-folder]");
    Console.WriteLine("  project-endpoint  Your Azure AI Foundry project endpoint URL");
    Console.WriteLine("  knowledge-folder  Path to the folder containing knowledge docs (default: ../knowledge)");
    return;
}

string projectEndpoint = args[0];
string folderPath = args.Length > 1 ? args[1] : "../knowledge";

// Create the Foundry Project Client
AIProjectClient projectClient = new AIProjectClient(
    new Uri(projectEndpoint),
    new DefaultAzureCredential()
);

// Get the Persistent Agents Client
PersistentAgentsClient agentsClient = projectClient.GetPersistentAgentsClient();

if (!Directory.Exists(folderPath))
{
    Console.WriteLine($"Documents folder not found at {folderPath}.");
    Console.WriteLine("Create it and add your Contoso Pizza files (PDF, TXT, MD, etc.).");
    return;
}

List<string> uploadedFileIds = new();

// Upload each file in the knowledge folder
foreach (string filePath in Directory.GetFiles(folderPath))
{
    string fileName = Path.GetFileName(filePath);
    Console.WriteLine($"Uploading file: {fileName}");

    PersistentAgentFileInfo fileInfo = agentsClient.Files.UploadFile(filePath, PersistentAgentFilePurpose.Agents);
    uploadedFileIds.Add(fileInfo.Id);
}

Console.WriteLine("File upload complete.");

// Create a vector store from the uploaded files
PersistentAgentsVectorStore vectorStore = agentsClient.VectorStores.CreateVectorStore(
    name: "contoso-pizza-store-information",
    fileIds: uploadedFileIds
);

Console.WriteLine($"Created vector store with ID: {vectorStore.Id}");
Console.WriteLine($"Set this as PizzaBot:VectorStoreId in your user secrets.");
