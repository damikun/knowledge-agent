using KnowledgeAgent.Infrastructure.VectorStore.ClickHouse;
using Microsoft.Extensions.Options;

namespace KnowledgeAgent.Infrastructure.VectorStore;

internal sealed class VectorStoreOptionsValidator : IValidateOptions<VectorStoreOptions>
{
    public ValidateOptionsResult Validate(string? name, VectorStoreOptions options)
    {
        var provider = VectorStoreProviders.Normalize(options.Provider);
        if (provider is null)
        {
            return ValidateOptionsResult.Fail(VectorStoreProviders.UnsupportedMessage(options.Provider));
        }

        if (provider != VectorStoreProviders.ClickHouse)
        {
            return ValidateOptionsResult.Success;
        }

        var failures = new List<string>();
        if (string.IsNullOrWhiteSpace(options.ConnectionStringName))
        {
            failures.Add("VectorStore:ConnectionStringName is required when VectorStore:Provider is ClickHouse.");
        }

        if (!ClickHouseIdentifier.IsValid(options.Database))
        {
            failures.Add(ClickHouseIdentifier.InvalidMessage(options.Database, "VectorStore:Database"));
        }

        if (!ClickHouseIdentifier.IsValid(options.Table))
        {
            failures.Add(ClickHouseIdentifier.InvalidMessage(options.Table, "VectorStore:Table"));
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
