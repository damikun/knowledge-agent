using Microsoft.Extensions.AI;

namespace KnowledgeAgent.Infrastructure.AI.Providers.Voyage;

internal sealed class VoyageEmbeddingGenerator(VoyageClient client, Uri endpoint, string model, int? dimensions)
    : IEmbeddingGenerator<string, Embedding<float>>
{
    public const string InputTypeProperty = "voyage.input_type";

    private readonly EmbeddingGeneratorMetadata _metadata = new("voyage", endpoint, model, dimensions);

    public async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var inputs = values.ToList();
        var inputType = options?.AdditionalProperties?.TryGetValue(InputTypeProperty, out var value) == true ? value as string : null;
        var modelId = string.IsNullOrWhiteSpace(options?.ModelId) ? model : options.ModelId;
        var (vectors, totalTokens) = await client.EmbedAsync(modelId, inputs, inputType, options?.Dimensions ?? dimensions, cancellationToken);
        var embeddings = new GeneratedEmbeddings<Embedding<float>>(vectors.Select(vector => new Embedding<float>(vector) { ModelId = modelId }));
        if (totalTokens is { } tokens)
        {
            embeddings.Usage = new UsageDetails { InputTokenCount = tokens, TotalTokenCount = tokens };
        }

        return embeddings;
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is not null ? null
        : serviceType == typeof(EmbeddingGeneratorMetadata) ? _metadata
        : serviceType.IsInstanceOfType(this) ? this
        : null;

    public void Dispose() => client.Dispose();
}
