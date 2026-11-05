using KnowledgeAgent.Features.Sources;
using KnowledgeAgent.Features.Sources.Chunking;
using KnowledgeAgent.Features.Sources.Ingestion;
using KnowledgeAgent.Infrastructure.AI.Chat;
using KnowledgeAgent.Infrastructure.AI.Profiles;
using KnowledgeAgent.TestKit;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KnowledgeAgent.Tests.Features.Sources;

public sealed class ChunkContextualizerTests
{
    private const string Document = """
        # Returns policy

        Intro text about the shop.

        ## Refunds

        Refunds are paid within 30 days of the return.

        ### Gift cards

        Gift cards cannot be refunded.

        ## Exchanges

        Exchanges are free within 14 days.
        """;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Chunks_get_their_title_and_section_path()
    {
        var chunks = new[]
        {
            Chunk(0, "Intro text about the shop."),
            Chunk(1, "Refunds are paid within 30 days of the return."),
            Chunk(2, "Gift cards cannot be refunded."),
            Chunk(3, "Exchanges are free within 14 days.", page: 2),
        };

        var contexts = ChunkContextualizer.HeadingContexts("Shop handbook", Document, chunks);

        contexts.ShouldBe(
        [
            "Shop handbook > Returns policy",
            "Shop handbook > Returns policy > Refunds",
            "Shop handbook > Returns policy > Refunds > Gift cards",
            "Shop handbook > Returns policy > Exchanges (page 2)",
        ]);
    }

    [Fact]
    public void A_heading_that_repeats_the_title_is_not_repeated()
    {
        var contexts = ChunkContextualizer.HeadingContexts("Returns policy", Document, [Chunk(0, "Intro text about the shop.")]);

        contexts.ShouldBe(["Returns policy"]);
    }

    [Fact]
    public void Long_documents_send_only_the_part_around_the_chunk()
    {
        var document = new string('a', 30_000) + "THE CHUNK" + new string('b', 30_000);

        var window = ChunkContextualizer.Window(document, "THE CHUNK");

        window.Length.ShouldBe(ChunkContextualizer.DocumentWindowChars);
        window.ShouldContain("THE CHUNK");
        ChunkContextualizer.Window("short", "short").ShouldBe("short");
    }

    [Fact]
    public async Task Off_and_headings_modes_never_call_a_model()
    {
        var factory = new CountingFactory();
        var chunks = new[] { Chunk(0, "Refunds are paid within 30 days of the return.") };

        var off = await Contextualizer(ContextualizeMode.Off, factory).ContextualizeAsync(Source(), Document, chunks, Ct);
        var headings = await Contextualizer(ContextualizeMode.Headings, factory).ContextualizeAsync(Source(), Document, chunks, Ct);
        var auto = await Contextualizer(ContextualizeMode.Auto, factory).ContextualizeAsync(Source(), Document, chunks, Ct);

        off.ShouldBe([string.Empty]);
        headings.ShouldBe(["Handbook > Returns policy > Refunds"]);
        auto.ShouldBe(headings, "no model was chosen for the contextualizer profile");
        factory.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task With_a_model_each_chunk_also_gets_a_written_context()
    {
        var client = new ScriptedChatClient { FallbackText = "This passage explains when refunds are paid after a return." };
        var factory = new CountingFactory(client);
        var chunks = new[] { Chunk(0, "Refunds are paid within 30 days of the return."), Chunk(1, "Exchanges are free within 14 days.") };

        var contexts = await Contextualizer(ContextualizeMode.Model, factory).ContextualizeAsync(Source(), Document, chunks, Ct);

        contexts[0].ShouldBe("Handbook > Returns policy > Refunds. This passage explains when refunds are paid after a return.");
        contexts[1].ShouldStartWith("Handbook > Returns policy > Exchanges. ");
        factory.Calls.ShouldBe(1);
        client.Requests.Count.ShouldBe(2);
        client.Requests[0].Messages[0].Text.ShouldBe(ChunkContextualizer.Instructions);
        client.Requests[0].Messages[1].Text.ShouldContain("<passage>\nRefunds are paid within 30 days of the return.\n</passage>");
        client.Requests[0].Messages[1].Text.ShouldContain("<document>");
    }

    [Fact]
    public async Task A_failing_model_leaves_the_heading_context()
    {
        var client = new ScriptedChatClient().ThenThrow(new HttpRequestException("rate limited"));
        var contexts = await Contextualizer(ContextualizeMode.Model, new CountingFactory(client))
            .ContextualizeAsync(Source(), Document, [Chunk(0, "Gift cards cannot be refunded.")], Ct);

        contexts.ShouldBe(["Handbook > Returns policy > Refunds > Gift cards"]);
    }

    private static ChunkContextualizer Contextualizer(ContextualizeMode mode, CountingFactory factory)
    {
        var services = new ServiceCollection().AddSingleton<IChatClientFactory>(factory).BuildServiceProvider();
        var options = new StaticOptionsMonitor(new IngestionOptions { Contextualize = mode, ModelContextParallelism = 1 });
        return new ChunkContextualizer(services, options, NullLogger<ChunkContextualizer>.Instance);
    }

    private static Source Source() => new() { Id = Guid.NewGuid(), Title = "Handbook" };

    private static TextChunk Chunk(int index, string content, int? page = null) => new(index, content, page, 10);

    private sealed class CountingFactory(IChatClient? client = null) : IChatClientFactory
    {
        public int Calls { get; private set; }

        public Task<ChatModelHandle> CreateAsync(
            string profileName,
            ModelProfilePatch? promptPatch = null,
            ModelProfilePatch? requestPatch = null,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            var profile = new ResolvedModelProfile
            {
                Name = profileName,
                Kind = KnowledgeAgent.Infrastructure.AI.ModelKind.Chat,
                ProviderKey = "fake",
                ProviderType = "fake",
                Model = "fake-small",
                Source = ModelProfileSource.Configuration,
            };
            return Task.FromResult(new ChatModelHandle(client ?? new ScriptedChatClient(), profile, () => new ChatOptions()));
        }
    }

    private sealed class StaticOptionsMonitor(IngestionOptions value) : IOptionsMonitor<IngestionOptions>
    {
        public IngestionOptions CurrentValue => value;

        public IngestionOptions Get(string? name) => value;

        public IDisposable? OnChange(Action<IngestionOptions, string?> listener) => null;
    }
}
