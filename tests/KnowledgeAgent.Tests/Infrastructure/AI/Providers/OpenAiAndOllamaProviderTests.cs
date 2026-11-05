using System.Text.Json.Nodes;
using KnowledgeAgent.Infrastructure.AI;
using KnowledgeAgent.Infrastructure.AI.Embeddings;
using KnowledgeAgent.Infrastructure.AI.Profiles;
using KnowledgeAgent.Infrastructure.AI.Providers.Ollama;
using KnowledgeAgent.Infrastructure.AI.Providers.OpenAI;
using KnowledgeAgent.Tests.Infrastructure.AI.Support;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace KnowledgeAgent.Tests.Infrastructure.AI.Providers;

public sealed class OpenAiAndOllamaProviderTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void OpenAI_clients_are_created_without_network_access()
    {
        var provider = new OpenAiProvider(NullLogger<OpenAiProvider>.Instance);
        var options = new AiProviderOptions { Type = "openai", ApiKey = "sk-openai-test" };

        using var chat = provider.CreateChatClient(options, Profile("gpt-5-mini", ModelKind.Chat));
        using var embeddings = provider.CreateEmbeddingGenerator(options, Profile("text-embedding-3-small", ModelKind.Embedding, dimensions: 256));

        chat.GetService<ChatClientMetadata>().ShouldNotBeNull().DefaultModelId.ShouldBe("gpt-5-mini");
        var metadata = embeddings.GetService<EmbeddingGeneratorMetadata>().ShouldNotBeNull();
        metadata.DefaultModelId.ShouldBe("text-embedding-3-small");
        metadata.DefaultModelDimensions.ShouldBe(256);
    }

    [Fact]
    public void OpenAI_compatible_servers_work_without_a_key()
    {
        var provider = new OpenAiProvider(NullLogger<OpenAiProvider>.Instance);
        var options = new AiProviderOptions { Type = "openai", Endpoint = "http://localhost:8000/v1" };

        provider.IsConfigured(options).ShouldBeTrue();
        provider.IsConfigured(new AiProviderOptions { Type = "openai" }).ShouldBeFalse();
        provider.IsConfigured(new AiProviderOptions { Type = "openai", Endpoint = "localhost:8000" }).ShouldBeFalse();

        using var chat = provider.CreateChatClient(options, Profile("llama-3.3", ModelKind.Chat));
        var metadata = chat.GetService<ChatClientMetadata>().ShouldNotBeNull();
        metadata.ProviderUri.ShouldNotBeNull().ToString().ShouldStartWith("http://localhost:8000/v1");
    }

    [Fact]
    public async Task OpenAI_chat_request_maps_model_limits_reasoning_and_tools()
    {
        await using var server = await CapturingHttpServer.StartAsync();
        server.Responder = _ => CannedResponse.Json("""
            {"id":"chatcmpl-1","object":"chat.completion","created":1700000000,"model":"gpt-5-mini",
             "choices":[{"index":0,"message":{"role":"assistant","content":"Hi from fake OpenAI"},"finish_reason":"stop"}],
             "usage":{"prompt_tokens":5,"completion_tokens":3,"total_tokens":8}}
            """);
        using var host = AiTestHost.Create(TestConfig.Base().With(
            ("AI:Providers:openai:ApiKey", "sk-openai-test"),
            ("AI:Providers:openai:Endpoint", new Uri(server.BaseAddress, "v1").ToString()),
            ("AI:Profiles:utility:Provider", "openai"),
            ("AI:Profiles:utility:Temperature", "0.3")));

        var handle = await host.ChatClients.CreateAsync("utility", cancellationToken: Ct);
        var options = handle.CreateOptions();
        options.Tools = [ChatClientFactoryTests.SearchTool()];
        var response = await handle.Client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")], options, Ct);

        response.Text.ShouldBe("Hi from fake OpenAI");
        var request = server.LastRequest;
        request.PathAndQuery.ShouldBe("/v1/chat/completions");
        request.Header("Authorization").ShouldBe("Bearer sk-openai-test");
        var body = request.Json;
        body["model"]!.GetValue<string>().ShouldBe("gpt-5-mini");
        body["reasoning_effort"]!.GetValue<string>().ShouldBe("low");
        body.ContainsKey("temperature").ShouldBeFalse();
        ((body["max_completion_tokens"] ?? body["max_tokens"])!.GetValue<int>()).ShouldBe(2000);
        body["tools"]!.AsArray().ShouldHaveSingleItem()!["function"]!["name"]!.GetValue<string>().ShouldBe("search_knowledge_base");
    }

    [Fact]
    public async Task OpenAI_embeddings_request_carries_model_and_dimensions()
    {
        await using var server = await CapturingHttpServer.StartAsync();
        server.Responder = request =>
        {
            var base64 = request.Json["encoding_format"]?.GetValue<string>() == "base64";
            var inputs = request.Json["input"] is JsonArray array ? array.Count : 1;
            var data = new JsonArray();
            for (var i = 0; i < inputs; i++)
            {
                float[] vector = [0.1f * (i + 1), 0.2f, 0.3f, 0.4f];
                JsonNode embedding = base64
                    ? JsonValue.Create(Convert.ToBase64String(vector.SelectMany(BitConverter.GetBytes).ToArray()))
                    : new JsonArray(vector.Select(v => (JsonNode)JsonValue.Create(v)).ToArray());
                data.Add(new JsonObject { ["object"] = "embedding", ["index"] = i, ["embedding"] = embedding });
            }

            var response = new JsonObject
            {
                ["object"] = "list",
                ["data"] = data,
                ["model"] = "text-embedding-3-small",
                ["usage"] = new JsonObject { ["prompt_tokens"] = 3, ["total_tokens"] = 3 },
            };
            return CannedResponse.Json(response.ToJsonString());
        };

        using var host = AiTestHost.Create(TestConfig.Base().With(
            ("AI:Providers:openai:ApiKey", "sk-openai-test"),
            ("AI:Providers:openai:Endpoint", new Uri(server.BaseAddress, "v1").ToString()),
            ("AI:Profiles:embedding:Provider", "openai"),
            ("AI:Profiles:embedding:Dimensions", "4")));

        var result = await host.Embeddings.EmbedAsync(["alpha", "beta"], EmbeddingPurpose.Document, Ct);

        result.ModelKey.ShouldBe("openai:text-embedding-3-small:4");
        result.Vectors.Count.ShouldBe(2);
        result.Vectors[1].Span[0].ShouldBe(0.2f, 1e-6f);

        var body = server.LastRequest.Json;
        server.LastRequest.PathAndQuery.ShouldBe("/v1/embeddings");
        body["model"]!.GetValue<string>().ShouldBe("text-embedding-3-small");
        body["dimensions"]!.GetValue<int>().ShouldBe(4);
        body["input"]!.AsArray().Select(n => n!.GetValue<string>()).ShouldBe(["alpha", "beta"]);
    }

    [Fact]
    public void Ollama_clients_are_created_without_network_access()
    {
        var provider = new OllamaAiProvider();
        var options = new AiProviderOptions { Type = "ollama", Endpoint = "http://localhost:11434" };

        provider.IsConfigured(options).ShouldBeTrue();
        provider.IsConfigured(new AiProviderOptions { Type = "ollama" }).ShouldBeFalse();
        provider.IsConfigured(new AiProviderOptions { Type = "ollama", Endpoint = "ftp://localhost" }).ShouldBeFalse();

        using var chat = provider.CreateChatClient(options, Profile("qwen3", ModelKind.Chat));
        using var embeddings = provider.CreateEmbeddingGenerator(options, Profile("nomic-embed-text", ModelKind.Embedding));

        var chatMetadata = chat.GetService<ChatClientMetadata>().ShouldNotBeNull();
        chatMetadata.DefaultModelId.ShouldBe("qwen3");
        chatMetadata.ProviderUri.ShouldBe(new Uri("http://localhost:11434"));
        embeddings.GetService<EmbeddingGeneratorMetadata>().ShouldNotBeNull().DefaultModelId.ShouldBe("nomic-embed-text");
    }

    private static ResolvedModelProfile Profile(string model, ModelKind kind, int? dimensions = null) => new()
    {
        Name = kind == ModelKind.Embedding ? "embedding" : "utility",
        Kind = kind,
        ProviderKey = "test",
        ProviderType = "test",
        Model = model,
        Dimensions = dimensions,
        Source = ModelProfileSource.Configuration,
    };
}
