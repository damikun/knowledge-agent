using System.Net;
using KnowledgeAgent.Features.Prompts;
using KnowledgeAgent.Features.Search.Reranking;
using KnowledgeAgent.Infrastructure.AI;
using KnowledgeAgent.Infrastructure.AI.Embeddings;
using KnowledgeAgent.Infrastructure.AI.Profiles;
using KnowledgeAgent.Infrastructure.AI.Providers.Voyage;
using KnowledgeAgent.TestKit;
using KnowledgeAgent.Tests.Infrastructure.AI.Support;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeAgent.Tests.Features.Search.Reranking;

public sealed class RerankTests
{
    private const string VoyageKey = "pa-test-voyage-key-123456";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Auto_reranks_with_voyage_when_it_has_a_key()
    {
        await using var server = await CapturingHttpServer.StartAsync();
        server.Responder = _ => CannedResponse.Json("""
            {"object":"list","data":[{"index":1,"relevance_score":0.91},{"index":0,"relevance_score":0.18}],"model":"rerank-2.5","usage":{"total_tokens":21}}
            """);
        using var host = AiTestHost.Create(Voyage(server, VoyageKey));
        var reranker = host.Get<IRerankService>();

        (await reranker.DescribeAsync(Ct)).ShouldBe("voyage/rerank-2.5");
        var result = await reranker.RerankAsync("How many vacation days?", ["Cafeteria hours", "27 vacation days per year"], Ct);

        result.ShouldNotBeNull();
        result.Model.ShouldBe("voyage/rerank-2.5");
        result.Scores.ShouldBe([0.18, 0.91]);
        var request = server.LastRequest;
        request.Method.ShouldBe("POST");
        request.PathAndQuery.ShouldBe("/v1/rerank");
        request.Header("Authorization").ShouldBe("Bearer " + VoyageKey);
        request.Json["query"]!.GetValue<string>().ShouldBe("How many vacation days?");
        request.Json["documents"]!.AsArray().Select(document => document!.GetValue<string>()).ShouldBe(["Cafeteria hours", "27 vacation days per year"]);
        request.Json["model"]!.GetValue<string>().ShouldBe("rerank-2.5");
        request.Json["truncation"]!.GetValue<bool>().ShouldBeTrue();
    }

    [Fact]
    public async Task Voyage_errors_carry_the_detail_but_never_the_key()
    {
        await using var server = await CapturingHttpServer.StartAsync();
        server.Responder = _ => CannedResponse.Json("""{"detail":"Provided API key is invalid."}""", status: 401);
        using var host = AiTestHost.Create(Voyage(server, VoyageKey));

        var error = await Should.ThrowAsync<VoyageApiException>(() => host.Get<IRerankService>().RerankAsync("q", ["a", "b"], Ct));

        error.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        error.Message.ShouldContain("Provided API key is invalid.");
        error.Message.ShouldNotContain(VoyageKey);
    }

    [Fact]
    public async Task Without_a_reranker_key_auto_reranks_nothing()
    {
        using var host = AiTestHost.Create(TestConfig.Base().With(RerankProfile("auto")).With(("AI:Providers:voyage:Type", "voyage"), ("AI:Providers:voyage:ApiKey", "")));
        var reranker = host.Get<IRerankService>();

        (await reranker.DescribeAsync(Ct)).ShouldBeNull();
        (await reranker.RerankAsync("q", ["a", "b"], Ct)).ShouldBeNull();
        var error = await Should.ThrowAsync<ModelNotConfiguredException>(() => host.Resolver.ResolveAsync("rerank", cancellationToken: Ct));
        error.Message.ShouldContain("rerank provider");
        error.Message.ShouldContain("voyage (not configured");
    }

    [Fact]
    public async Task Chosen_chat_providers_rerank_with_an_llm_through_the_rerank_prompt()
    {
        var model = new ScriptedChatClient().ThenText("Ratings:\n```json\n{\"1\": 3, \"2\": 0, \"3\": 1.5}\n```");
        var prompts = new StubPromptService();
        using var host = AiTestHost.Create(
            TestConfig.WithFakeProviders().With(RerankProfile("fake")),
            services =>
            {
                services.AddSingleton<IAiProvider>(new FakeAiProvider(_ => model));
                services.AddSingleton<IPromptService>(prompts);
            });
        var reranker = host.Get<IRerankService>();

        var result = await reranker.RerankAsync("vacation days", ["Holiday policy", "Cafeteria", "Leave carry-over"], Ct);

        result.ShouldNotBeNull();
        result.Scores.ShouldBe([1, 0, 0.5]);
        result.Model.ShouldStartWith("fake/");
        prompts.Variables.ShouldNotBeNull()["query"].ShouldBe("vacation days");
        var passages = (IList<object?>)prompts.Variables["passages"]!;
        passages.Count.ShouldBe(3);
        model.Requests.ShouldHaveSingleItem().Messages.Single().Text.ShouldBe("rendered rerank prompt");
    }

    [Fact]
    public async Task Rerank_profiles_default_to_the_providers_rerank_model_or_chat_model()
    {
        using var host = AiTestHost.Create(
            TestConfig.Base()
                .With(RerankProfile("anthropic"))
                .With(
                    ("AI:Providers:anthropic:ApiKey", "sk-ant-test"),
                    ("AI:Providers:voyage:Type", "voyage"),
                    ("AI:Providers:voyage:ApiKey", VoyageKey),
                    ("AI:Providers:vectors:Type", EmbeddingsOnlyProvider.TypeName)),
            services => services.AddSingleton<IAiProvider>(new EmbeddingsOnlyProvider()));

        (await host.Resolver.ResolveAsync("rerank", cancellationToken: Ct)).Model.ShouldBe("claude-opus-5-5", "no rerank model configured: the chat model");

        host.Reconfigure(("AI:Providers:anthropic:DefaultRerankModel", "claude-haiku-5-5"));
        (await host.Resolver.ResolveAsync("rerank", cancellationToken: Ct)).Model.ShouldBe("claude-haiku-5-5");

        host.Reconfigure(("AI:Profiles:rerank:Provider", "voyage"));
        var voyage = await host.Resolver.ResolveAsync("rerank", cancellationToken: Ct);
        voyage.Model.ShouldBe(VoyageAiProvider.DefaultRerankModel);
        voyage.Kind.ShouldBe(ModelKind.Rerank);

        host.Reconfigure(("AI:Profiles:rerank:Provider", "vectors"));
        (await Should.ThrowAsync<ModelNotConfiguredException>(() => host.Resolver.ResolveAsync("rerank", cancellationToken: Ct)))
            .Message.ShouldContain("does not support reranking");
    }

    [Fact]
    public async Task The_auto_rerank_order_is_configurable()
    {
        using var host = AiTestHost.Create(
            TestConfig.WithFakeProviders().With(RerankProfile("auto")).With(
                ("AI:Providers:voyage:Type", "voyage"),
                ("AI:Providers:voyage:ApiKey", ""),
                ("AI:AutoRerankProviders:0", "voyage"),
                ("AI:AutoRerankProviders:1", "fake2")),
            services => services.AddSingleton<IAiProvider>(new FakeAiProvider()));

        var profile = await host.Resolver.ResolveAsync("rerank", cancellationToken: Ct);

        profile.ProviderKey.ShouldBe("fake2", "voyage has no key, so the next configured provider reranks");
        profile.Model.ShouldBe("fake2-chat");
    }

    [Fact]
    public async Task Rerank_profiles_get_chat_clients_but_embedding_profiles_do_not()
    {
        using var host = AiTestHost.Create(
            TestConfig.WithFakeProviders().With(RerankProfile("fake")),
            services => services.AddSingleton<IAiProvider>(new FakeAiProvider()));

        var handle = await host.ChatClients.CreateAsync("rerank", cancellationToken: Ct);

        handle.Profile.Kind.ShouldBe(ModelKind.Rerank);
        await Should.ThrowAsync<ModelNotConfiguredException>(() => host.ChatClients.CreateAsync("embedding", cancellationToken: Ct));
    }

    [Theory]
    [InlineData("""{"1": 3, "2": 2, "3": 0}""", new[] { 1, 2 / 3d, 0 })]
    [InlineData("noise {\"2\": 7, \"9\": 3} noise", new[] { 0, 1d, 0 })]
    [InlineData("""{"[1]": 1, "3": -2}""", new[] { 1 / 3d, 0, 0 })]
    public void Llm_ratings_become_scores_between_zero_and_one(string reply, double[] expected)
    {
        LlmReranker.Parse(reply, 3).Select(score => Math.Round(score, 6)).ShouldBe(expected.Select(score => Math.Round(score, 6)));
    }

    [Theory]
    [InlineData("no json")]
    [InlineData("{\"x\": 3}")]
    [InlineData("{ broken")]
    public void Unreadable_llm_ratings_are_errors(string reply) => Should.Throw<FormatException>(() => LlmReranker.Parse(reply, 3));

    [Fact]
    public async Task Voyage_embeddings_send_the_input_type_and_dimensions()
    {
        await using var server = await CapturingHttpServer.StartAsync();
        server.Responder = request =>
        {
            var count = request.Json["input"]!.AsArray().Count;
            var data = string.Join(",", Enumerable.Range(0, count).Reverse().Select(index => $$"""{"object":"embedding","index":{{index}},"embedding":[{{index + 1}}.0,0.5]}"""));
            return CannedResponse.Json($$$"""{"object":"list","data":[{{{data}}}],"model":"voyage-4","usage":{"total_tokens":7}}""");
        };
        using var host = AiTestHost.Create(Voyage(server, VoyageKey).With(("AI:Profiles:embedding:Provider", "voyage")));

        var documents = await host.Embeddings.EmbedAsync(["first", "second"], EmbeddingPurpose.Document, Ct);
        var documentRequest = server.LastRequest.Json;
        var query = await host.Embeddings.EmbedAsync(["question"], EmbeddingPurpose.Query, Ct);
        var queryRequest = server.LastRequest.Json;

        documents.ModelKey.ShouldBe("voyage:voyage-4:2");
        documents.Vectors.Select(vector => vector.Span[0]).ShouldBe([1f, 2f], "vectors are matched to inputs by index");
        documentRequest["input_type"]!.GetValue<string>().ShouldBe("document");
        documentRequest["model"]!.GetValue<string>().ShouldBe("voyage-4");
        documentRequest["output_dimension"]!.GetValue<int>().ShouldBe(1024);
        queryRequest["input_type"]!.GetValue<string>().ShouldBe("query");
        query.Vectors.Count.ShouldBe(1);
    }

    private static Dictionary<string, string?> Voyage(CapturingHttpServer server, string apiKey) => TestConfig.Base()
        .With(RerankProfile("auto"))
        .With(
            ("AI:Providers:voyage:Type", "voyage"),
            ("AI:Providers:voyage:ApiKey", apiKey),
            ("AI:Providers:voyage:Endpoint", new Uri(server.BaseAddress, "v1/").ToString()),
            ("AI:Providers:voyage:DefaultRerankModel", "rerank-2.5"),
            ("AI:Providers:voyage:DefaultEmbeddingModel", "voyage-4"),
            ("AI:Providers:voyage:DefaultEmbeddingDimensions", "1024"));

    private static (string Key, string? Value)[] RerankProfile(string provider) =>
    [
        ("AI:Profiles:rerank:Provider", provider),
        ("AI:Profiles:rerank:Kind", "Rerank"),
    ];

    // A provider with embeddings only, the one kind that can neither rerank nor chat.
    private sealed class EmbeddingsOnlyProvider : IAiProvider
    {
        public const string TypeName = "embeddings-only";

        public string Type => TypeName;

        public bool SupportsChat => false;

        public bool SupportsEmbeddings => true;

        public bool IsConfigured(AiProviderOptions options) => true;

        public IChatClient CreateChatClient(AiProviderOptions options, ResolvedModelProfile profile) => throw new NotSupportedException();

        public IEmbeddingGenerator<string, Embedding<float>> CreateEmbeddingGenerator(AiProviderOptions options, ResolvedModelProfile profile) =>
            new FakeEmbeddingGenerator();
    }

    private sealed class StubPromptService : IPromptService
    {
        public IReadOnlyDictionary<string, object?>? Variables { get; private set; }

        public Task<RenderedPrompt> RenderAsync(string name, IReadOnlyDictionary<string, object?> variables, PromptSelector? selector = null, CancellationToken cancellationToken = default)
        {
            name.ShouldBe(PromptNames.Rerank);
            Variables = variables;
            return Task.FromResult(new RenderedPrompt { Name = name, Version = 4, Type = PromptType.Text, Text = "rendered rerank prompt" });
        }

        public Task<PromptTemplate> GetTemplateAsync(string name, PromptSelector? selector = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<PromptSummary>> ListAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public string RenderTemplate(string template, IReadOnlyDictionary<string, object?> variables) => throw new NotSupportedException();
    }
}
