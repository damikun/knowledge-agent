using KnowledgeAgent.Features.Agents;
using KnowledgeAgent.Features.Chat;
using KnowledgeAgent.Features.Prompts;
using KnowledgeAgent.Features.Search;
using KnowledgeAgent.Features.Sources;
using KnowledgeAgent.Features.Sources.Folders;
using KnowledgeAgent.Infrastructure.AI.Chat;
using KnowledgeAgent.Infrastructure.VectorStore;
using KnowledgeAgent.Infrastructure.VectorStore.InMemory;
using KnowledgeAgent.Tests.Features.Chat.Fakes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;

namespace KnowledgeAgent.Tests.Features.Chat.Support;

internal sealed class AgentTestHost : IDisposable
{
    public static readonly DateTimeOffset Now = new(2026, 10, 22, 9, 30, 0, TimeSpan.Zero);

    private readonly IHost _host;

    public AgentTestHost(IReadOnlyDictionary<string, string?>? settings = null, Action<IServiceCollection>? configureServices = null)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = Environments.Development });
        builder.Configuration.AddInMemoryCollection(settings ?? new Dictionary<string, string?>());
        builder.ConfigureContainer(new DefaultServiceProviderFactory(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }));
        builder.Services.AddLogging();
        builder.Services.AddSingleton<TimeProvider>(Time);

        builder.AddChat();
        builder.AddAgents();

        builder.Services.AddSingleton<IChatClientFactory>(ChatClients);
        builder.Services.AddSingleton<IPromptService>(Prompts);
        builder.Services.AddSingleton<IKnowledgeSearch>(Search);
        builder.Services.AddSingleton<IVectorStore>(VectorStore);
        builder.Services.AddScoped<IExactSearch, ExactSearch>();
        builder.Services.Replace(ServiceDescriptor.Scoped<ISourceRepository>(_ => Sources));
        builder.Services.Replace(ServiceDescriptor.Scoped<IConversationRepository>(_ => Conversations));
        builder.Services.Replace(ServiceDescriptor.Scoped<IAgentRepository>(_ => Agents));
        builder.Services.AddScoped<ISourceFolderRepository>(_ => Folders);
        configureServices?.Invoke(builder.Services);

        _host = builder.Build();
    }

    public FakeChatClientFactory ChatClients { get; } = new();
    public FakePromptService Prompts { get; } = new();
    public FakeKnowledgeSearch Search { get; } = new();

    // grep_sources runs the real exact search over this store and the source repository below.
    public InMemoryVectorStore VectorStore { get; } = new();
    public InMemorySourceRepository Sources { get; } = new();
    public InMemoryConversationRepository Conversations { get; } = new();
    public InMemoryAgentRepository Agents { get; } = new();
    public InMemorySourceFolderRepository Folders { get; } = new();
    public FakeTimeProvider Time { get; } = new(Now);

    public IServiceProvider Services => _host.Services;

    public Task<List<ChatStreamEvent>> ChatAsync(string message, Guid? conversationId = null, CancellationToken cancellationToken = default) =>
        ChatAsync(new ChatRequestDto(conversationId, message), cancellationToken);

    public async Task<List<ChatStreamEvent>> ChatAsync(ChatRequestDto request, CancellationToken cancellationToken = default)
    {
        await using var scope = _host.Services.CreateAsyncScope();
        var chat = scope.ServiceProvider.GetRequiredService<IChatService>();
        var events = new List<ChatStreamEvent>();
        await foreach (var streamEvent in chat.StreamAsync(request, cancellationToken))
        {
            events.Add(streamEvent);
        }

        return events;
    }

    public async Task<T> WithChatServiceAsync<T>(Func<IChatService, Task<T>> action)
    {
        await using var scope = _host.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<IChatService>());
    }

    public ConversationTurn SingleTurn(Guid conversationId) => Conversations.Get(conversationId).Turns.ShouldHaveSingleItem();

    public void Dispose() => _host.Dispose();
}
