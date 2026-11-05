using KnowledgeAgent.Features.Prompts;
using KnowledgeAgent.Features.Prompts.Langfuse;
using KnowledgeAgent.Features.Prompts.Rendering;
using KnowledgeAgent.Features.Prompts.Seeding;
using KnowledgeAgent.Infrastructure.Langfuse;
using KnowledgeAgent.TestKit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KnowledgeAgent.Tests.Features.Prompts.Support;

internal sealed class PromptHarness : IDisposable
{
    private readonly HttpClient _http;

    public PromptHarness(
        FakeLangfuseServer? server,
        Action<PromptOptions>? configurePrompts = null,
        Action<LangfuseOptions>? configureLangfuse = null,
        TimeProvider? timeProvider = null,
        HttpMessageHandler? handler = null,
        ILoggerFactory? loggerFactory = null)
    {
        PromptOptions = new PromptOptions();
        configurePrompts?.Invoke(PromptOptions);
        LangfuseOptions = server is null ? new LangfuseOptions() : LangfuseOptionsFor(server);
        configureLangfuse?.Invoke(LangfuseOptions);

        Time = timeProvider ?? TimeProvider.System;
        LoggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        LangfuseClient.ConfigureHttpClient(_http, LangfuseOptions);
        Client = new LangfuseClient(_http);
        Renderer = new HandlebarsPromptRenderer(Time);
        Local = new LocalPromptCatalog();
        Links = new LangfuseLinkBuilder(Client, Options.Create(LangfuseOptions), Time, LoggerFactory.CreateLogger<LangfuseLinkBuilder>());
        Service = new PromptService(
            Renderer,
            Local,
            Client,
            Links,
            Options.Create(PromptOptions),
            Options.Create(LangfuseOptions),
            Time,
            LoggerFactory.CreateLogger<PromptService>());
    }

    public PromptOptions PromptOptions { get; }
    public LangfuseOptions LangfuseOptions { get; }
    public TimeProvider Time { get; }
    public ILoggerFactory LoggerFactory { get; }
    public LangfuseClient Client { get; }
    public HandlebarsPromptRenderer Renderer { get; }
    public LocalPromptCatalog Local { get; }
    public LangfuseLinkBuilder Links { get; }
    public PromptService Service { get; }

    public static LangfuseOptions LangfuseOptionsFor(FakeLangfuseServer server) => new()
    {
        BaseUrl = server.BaseAddress.ToString(),
        PublicKey = server.PublicKey,
        SecretKey = server.SecretKey,
    };

    public static LangfuseClient CreateClient(LangfuseOptions options, out HttpClient httpClient)
    {
        httpClient = new HttpClient();
        LangfuseClient.ConfigureHttpClient(httpClient, options);
        return new LangfuseClient(httpClient);
    }

    public PromptSeeder CreateSeeder() => new(
        Client,
        Local,
        Options.Create(PromptOptions),
        Options.Create(LangfuseOptions),
        Time,
        LoggerFactory.CreateLogger<PromptSeeder>());

    public void Dispose()
    {
        Service.Dispose();
        Links.Dispose();
        _http.Dispose();
    }
}
