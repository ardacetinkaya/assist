var builder = DistributedApplication.CreateBuilder(args);

// You will need to set the connection string to your own value
// You can do this using Visual Studio's "Manage User Secrets" UI, or on the command line:
//   cd this-project-directory
//   dotnet user-secrets set ConnectionStrings:openai "Endpoint=https://models.inference.ai.azure.com;Key=YOUR-API-KEY"
var openai = builder.AddConnectionString("openai");
var mongodb = builder.AddConnectionString("mongodb");
var webApp = builder.AddProject<Projects.Assist_Web>("aichatweb-app");
webApp.WithReference(openai)
    .WithReference(mongodb);

builder.Build().Run();
