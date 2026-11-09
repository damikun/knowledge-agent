using KnowledgeAgent.Features.Chat.Live;

namespace KnowledgeAgent.Tests.Features.Chat.Live;

public sealed class LiveOptionsTests
{
    [Fact]
    public void An_empty_provider_picks_postgres_only_when_the_database_is_postgres()
    {
        LiveProviders.UsesPostgres(null, hasDataSource: true).ShouldBeTrue();
        LiveProviders.UsesPostgres(" ", hasDataSource: true).ShouldBeTrue();
        LiveProviders.UsesPostgres(null, hasDataSource: false).ShouldBeFalse();
        LiveProviders.UsesPostgres("", hasDataSource: false).ShouldBeFalse();
    }

    [Fact]
    public void InProcess_stays_in_process_even_with_a_postgres_database()
    {
        LiveProviders.UsesPostgres(LiveProviders.InProcess, hasDataSource: true).ShouldBeFalse();
        Validate(LiveProviders.InProcess, hasDataSource: true).Succeeded.ShouldBeTrue();
        Validate(LiveProviders.InProcess, hasDataSource: false).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void An_unknown_provider_fails_validation()
    {
        var result = Validate("Redis", hasDataSource: true);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("Live:Provider 'Redis'");
    }

    [Fact]
    public void Postgres_without_a_postgres_database_fails_validation()
    {
        var result = Validate(LiveProviders.Postgres, hasDataSource: false);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("Persistence:Provider");
        Validate(LiveProviders.Postgres, hasDataSource: true).Succeeded.ShouldBeTrue();
        Validate(null, hasDataSource: false).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void Provider_names_ignore_case()
    {
        LiveProviders.UsesPostgres("postgres", hasDataSource: true).ShouldBeTrue();
        LiveProviders.UsesPostgres("inprocess", hasDataSource: true).ShouldBeFalse();
        LiveProviders.IsKnown("INPROCESS").ShouldBeTrue();
        Validate("POSTGRES", hasDataSource: true).Succeeded.ShouldBeTrue();
    }

    private static Microsoft.Extensions.Options.ValidateOptionsResult Validate(string? provider, bool hasDataSource) =>
        new LiveOptionsValidator(hasDataSource).Validate(null, new LiveOptions { Provider = provider });
}
