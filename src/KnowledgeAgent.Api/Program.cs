using KnowledgeAgent;
using KnowledgeAgent.Api;
using KnowledgeAgent.Infrastructure.Hosting;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddKnowledgeAgent();
builder.AddApiServices();

var app = builder.Build();

await app.Services.RunStartupInitializersAsync();

app.UseApiPipeline();
app.MapDefaultEndpoints();
app.MapKnowledgeAgentApi();

app.Run();

public partial class Program;
