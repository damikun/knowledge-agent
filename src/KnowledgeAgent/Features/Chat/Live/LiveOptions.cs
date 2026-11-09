using Microsoft.Extensions.Options;

namespace KnowledgeAgent.Features.Chat.Live;

public sealed class LiveOptions
{
    public const string SectionName = "Live";

    // Empty: Postgres when the database is PostgreSQL, InProcess otherwise.
    public string? Provider { get; set; }
}

public static class LiveProviders
{
    public const string InProcess = "InProcess";
    public const string Postgres = "Postgres";

    public static bool IsPostgres(string? provider) => string.Equals(provider, Postgres, StringComparison.OrdinalIgnoreCase);

    public static bool IsKnown(string? provider) =>
        string.IsNullOrWhiteSpace(provider) || IsPostgres(provider) || string.Equals(provider, InProcess, StringComparison.OrdinalIgnoreCase);

    public static bool UsesPostgres(string? provider, bool hasDataSource) =>
        hasDataSource && (string.IsNullOrWhiteSpace(provider) || IsPostgres(provider));
}

internal sealed class LiveOptionsValidator(bool hasDataSource) : IValidateOptions<LiveOptions>
{
    public ValidateOptionsResult Validate(string? name, LiveOptions options)
    {
        if (!LiveProviders.IsKnown(options.Provider))
        {
            return ValidateOptionsResult.Fail(
                $"Live:Provider '{options.Provider}' is not supported. Use {LiveProviders.Postgres} or {LiveProviders.InProcess}, or leave it empty.");
        }

        return LiveProviders.IsPostgres(options.Provider) && !hasDataSource
            ? ValidateOptionsResult.Fail("Live:Provider is Postgres but Persistence:Provider is not; use InProcess or leave it empty.")
            : ValidateOptionsResult.Success;
    }
}
