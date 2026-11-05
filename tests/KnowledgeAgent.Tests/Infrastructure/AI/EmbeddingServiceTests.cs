using KnowledgeAgent.Infrastructure.AI;
using KnowledgeAgent.Infrastructure.AI.Configuration;
using KnowledgeAgent.Infrastructure.AI.Embeddings;
using KnowledgeAgent.Infrastructure.AI.Profiles;
using KnowledgeAgent.TestKit;
using KnowledgeAgent.Tests.Infrastructure.AI.Support;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeAgent.Tests.Infrastructure.AI;

public sealed class EmbeddingServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Without_an_embedding_model_the_service_is_unavailable_and_says_what_to_configure()
    {
        using var host = AiTestHost.Create();

        (await host.Embeddings.IsAvailableAsync(Ct)).ShouldBeFalse();

        // Ingestion relies on this to tell a missing model from a transient failure.
        var error = await Should.ThrowAsync<ModelNotConfiguredException>(() => host.Embeddings.EmbedAsync(["text"], EmbeddingPurpose.Document, Ct));
        error.Message.ShouldContain("'embedding' profile");
        error.Message.ShouldContain("voyage");
    }

    [Fact]
    public async Task With_an_embedding_model_the_service_is_available_without_embedding_anything()
    {
        var generator = new FakeEmbeddingGenerator();
        using var host = CreateFakeHost(new FakeAiProvider(embeddingGenerator: generator));

        (await host.Embeddings.IsAvailableAsync(Ct)).ShouldBeTrue();
        generator.CallCount.ShouldBe(0);
    }

    [Fact]
    public async Task Sends_inputs_in_batches_of_64_and_keeps_their_order()
    {
        var generator = new FakeEmbeddingGenerator(dimensions: 16);
        using var host = CreateFakeHost(new FakeAiProvider(embeddingGenerator: generator));
        var inputs = Enumerable.Range(0, 150).Select(i => $"passage number {i}").ToList();

        var result = await host.Embeddings.EmbedAsync(inputs, cancellationToken: Ct);

        generator.CallCount.ShouldBe(3);
        result.Vectors.Count.ShouldBe(150);
        result.Dimensions.ShouldBe(16);
        result.ModelKey.ShouldBe("fake:fake-embed:16");
        result.Vectors[149].ToArray().ShouldBe(FakeEmbeddingGenerator.Embed("passage number 149", 16));
    }

    [Fact]
    public async Task Empty_input_does_not_call_the_provider()
    {
        var generator = new FakeEmbeddingGenerator(dimensions: 16);
        using var host = CreateFakeHost(new FakeAiProvider(embeddingGenerator: generator), ("AI:Profiles:embedding:Dimensions", "16"));

        var result = await host.Embeddings.EmbedAsync([], cancellationToken: Ct);

        generator.CallCount.ShouldBe(0);
        result.Vectors.ShouldBeEmpty();
        result.ModelKey.ShouldBe("fake:fake-embed:16");
    }

    [Theory]
    [InlineData("nomic-embed-text", EmbeddingPurpose.Query, "search_query: ")]
    [InlineData("nomic-embed-text:v1.5", EmbeddingPurpose.Document, "search_document: ")]
    [InlineData("text-embedding-3-small", EmbeddingPurpose.Query, "")]
    public async Task Nomic_models_get_task_prefixes(string model, EmbeddingPurpose purpose, string expectedPrefix)
    {
        var generator = new ScriptedEmbeddingGenerator();
        using var host = CreateFakeHost(new FakeAiProvider(embeddingGenerator: generator), ("AI:Profiles:embedding:Model", model));

        await host.Embeddings.EmbedAsync(["how do refunds work"], purpose, Ct);

        generator.AllInputs.ShouldBe([expectedPrefix + "how do refunds work"]);
        generator.Options.ShouldHaveSingleItem().ShouldNotBeNull().ModelId.ShouldBe(model);
    }

    [Fact]
    public async Task Configured_dimensions_are_passed_to_the_provider()
    {
        var generator = new ScriptedEmbeddingGenerator { DimensionsFor = _ => 32 };
        using var host = CreateFakeHost(new FakeAiProvider(embeddingGenerator: generator), ("AI:Profiles:embedding:Dimensions", "32"));

        var result = await host.Embeddings.EmbedAsync(["text"], cancellationToken: Ct);

        generator.Options.ShouldHaveSingleItem().ShouldNotBeNull().Dimensions.ShouldBe(32);
        result.ModelKey.ShouldBe("fake:fake-embed:32");
    }

    [Fact]
    public async Task Vectors_of_different_sizes_are_rejected()
    {
        var generator = new ScriptedEmbeddingGenerator { DimensionsFor = i => i < 70 ? 8 : 12 };
        using var host = CreateFakeHost(new FakeAiProvider(embeddingGenerator: generator));

        var error = await Should.ThrowAsync<InvalidOperationException>(
            () => host.Embeddings.EmbedAsync(Enumerable.Range(0, 100).Select(i => $"t{i}").ToList(), cancellationToken: Ct));

        error.Message.ShouldContain("different dimensions (8 and 12)");
    }

    [Fact]
    public async Task A_provider_returning_the_wrong_number_of_vectors_is_rejected()
    {
        var generator = new ScriptedEmbeddingGenerator { VectorsPerBatch = 1 };
        using var host = CreateFakeHost(new FakeAiProvider(embeddingGenerator: generator));

        var error = await Should.ThrowAsync<InvalidOperationException>(() => host.Embeddings.EmbedAsync(["a", "b"], cancellationToken: Ct));

        error.Message.ShouldContain("returned 1 vectors for 2 inputs");
    }

    [Fact]
    public async Task Model_switches_apply_to_the_next_call()
    {
        using var host = CreateFakeHost(new FakeAiProvider(embeddingGenerator: new FakeEmbeddingGenerator(dimensions: 32)), ("AI:Profiles:embedding:Dimensions", "32"));
        var before = await host.Embeddings.EmbedAsync(["text"], cancellationToken: Ct);

        host.Overrides.Set(new ModelProfileOverride { Name = "embedding", Provider = "fake", Model = "fake-embed-v2", Dimensions = 64 });
        var after = await host.Embeddings.EmbedAsync(["text"], cancellationToken: Ct);

        before.ModelKey.ShouldBe("fake:fake-embed:32");
        after.ModelKey.ShouldBe("fake:fake-embed-v2:64");
        after.Vectors[0].Length.ShouldBe(64);
    }

    [Fact]
    public async Task A_chat_kind_embedding_profile_is_rejected()
    {
        using var host = CreateFakeHost(new FakeAiProvider(), ("AI:Profiles:embedding:Kind", "Chat"));

        var error = await Should.ThrowAsync<ModelNotConfiguredException>(() => host.Embeddings.EmbedAsync(["text"], cancellationToken: Ct));

        error.Message.ShouldContain("Kind = Embedding");
    }

    [Fact]
    public void Model_key_format_is_provider_type_model_dimensions()
    {
        var profile = new ResolvedModelProfile
        {
            Name = "embedding",
            Kind = ModelKind.Embedding,
            ProviderKey = "my-openai",
            ProviderType = "openai",
            Model = "text-embedding-3-large",
            Source = ModelProfileSource.Configuration,
        };

        EmbeddingService.ModelKey(profile, 3072).ShouldBe("openai:text-embedding-3-large:3072");
    }

    private static AiTestHost CreateFakeHost(FakeAiProvider fake, params (string Key, string? Value)[] values) =>
        AiTestHost.Create(
            TestConfig.WithFakeProviders().With([("AI:Profiles:embedding:Provider", "fake"), .. values]),
            services => services.AddSingleton<IAiProvider>(fake));
}
