using KnowledgeAgent.Features.Integrations;
using KnowledgeAgent.Infrastructure.Persistence;
using KnowledgeAgent.Infrastructure.Web;
using KnowledgeAgent.Tests.Infrastructure.Persistence.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace KnowledgeAgent.Tests.Features.Integrations;

public sealed class PublicAddressTests : IAsyncDisposable
{
    private readonly FakeIntegrations _integrations = new();
    private IHost? _host;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask DisposeAsync()
    {
        _host?.Dispose();
        return ValueTask.CompletedTask;
    }

    [Theory]
    [InlineData("https://support.acme.com", "https://support.acme.com")]
    [InlineData("  https://Support.Acme.com/  ", "https://support.acme.com")]
    [InlineData("http://localhost:8080", "http://localhost:8080")]
    [InlineData("https://acme.com/support/", "https://acme.com/support")]
    [InlineData("https://acme.com:443/help", "https://acme.com/help")]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Accepts_an_origin_with_an_optional_base_path_and_trims_the_slash(string? input, string? expected)
    {
        PublicAddressStore.TryNormalize(input, out var url, out var error).ShouldBeTrue(error);
        url.ShouldBe(expected);
    }

    [Theory]
    [InlineData("support.acme.com")]
    [InlineData("ftp://acme.com")]
    [InlineData("/support")]
    [InlineData("https://acme.com/?team=1")]
    [InlineData("https://acme.com/#top")]
    [InlineData("https://admin:secret@acme.com")]
    public void Rejects_anything_but_a_plain_http_or_https_address(string input)
    {
        PublicAddressStore.TryNormalize(input, out var url, out var error).ShouldBeFalse();
        url.ShouldBeNull();
        error.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Saved_wins_over_configuration_which_wins_over_the_email_settings()
    {
        _integrations.Email = new EmailSettings { PortalUrl = "https://old.acme.com/" };
        Start(configured: null);
        (await GetAsync()).ShouldBe(new PublicAddressDto("https://old.acme.com", PublicAddressSources.Email));

        Start(configured: "https://config.acme.com/");
        (await GetAsync()).ShouldBe(new PublicAddressDto("https://config.acme.com", PublicAddressSources.Configuration));

        var saved = await InScopeAsync(address => address.SaveAsync(" https://support.acme.com/ ", Ct));
        saved.ShouldBe(new PublicAddressDto("https://support.acme.com", PublicAddressSources.Saved));
        (await GetAsync()).ShouldBe(saved);

        await InScopeAsync(address => address.SaveAsync("https://help.acme.com", Ct));
        (await GetAsync()).Url.ShouldBe("https://help.acme.com");
        (await CountRowsAsync()).ShouldBe(1);

        (await InScopeAsync(address => address.SaveAsync("", Ct))).ShouldBe(new PublicAddressDto("https://config.acme.com", PublicAddressSources.Configuration));
        (await CountRowsAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Nothing_set_means_no_address_and_an_invalid_configuration_is_ignored()
    {
        Start(configured: "not an address");

        (await GetAsync()).ShouldBe(new PublicAddressDto(null, null));
    }

    [Fact]
    public async Task An_invalid_address_is_not_saved()
    {
        Start(configured: null);

        var failure = await Should.ThrowAsync<ValidationFailedException>(() => InScopeAsync(address => address.SaveAsync("support.acme.com", Ct)));

        failure.Errors.Keys.ShouldBe(["url"]);
        (await CountRowsAsync()).ShouldBe(0);
    }

    private void Start(string? configured)
    {
        _host?.Dispose();
        _host = PersistenceHost.Build(
            new Dictionary<string, string?>
            {
                ["Persistence:Provider"] = "InMemory",
                ["Persistence:InMemoryDatabaseName"] = $"public-address-{Guid.NewGuid():N}",
                [PublicAddressStore.ConfigurationKey] = configured,
            },
            configure: builder =>
            {
                builder.Services.AddSingleton<IIntegrationSettings>(_integrations);
                builder.Services.AddScoped<IPublicAddress, PublicAddressStore>();
            });
    }

    private Task<PublicAddressDto> GetAsync() => InScopeAsync(address => address.GetAsync(Ct));

    private async Task<int> CountRowsAsync()
    {
        await using var scope = _host!.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<KnowledgeDbContext>().IntegrationSettings.CountAsync(item => item.Key == PublicAddressStore.Key, Ct);
    }

    private async Task<T> InScopeAsync<T>(Func<IPublicAddress, Task<T>> action)
    {
        await using var scope = _host!.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<IPublicAddress>());
    }

    private sealed class FakeIntegrations : IIntegrationSettings
    {
        public EmailSettings Email { get; set; } = new();

        public event Action? Changed
        {
            add { }
            remove { }
        }

        public Task<EmailSettings> GetEmailAsync(CancellationToken cancellationToken = default) => Task.FromResult(Email);

        public Task<SlackSettings> GetSlackAsync(CancellationToken cancellationToken = default) => Task.FromResult(new SlackSettings());

        public Task<(bool Email, bool Slack)> GetSavedAsync(CancellationToken cancellationToken = default) => Task.FromResult((false, false));

        public Task SaveEmailAsync(EmailSettings settings, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task SaveSlackAsync(SlackSettings settings, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
