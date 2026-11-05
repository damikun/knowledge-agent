using System.Text.Json;
using KnowledgeAgent.Infrastructure.Persistence;
using KnowledgeAgent.Infrastructure.Web;
using Microsoft.Extensions.Configuration;

namespace KnowledgeAgent.Features.Integrations;

public sealed record PublicAddressDto(string? Url, string? Source);

public sealed record SavePublicAddressRequest(string? Url);

public static class PublicAddressSources
{
    public const string Saved = "saved";
    public const string Configuration = "configuration";
    public const string Email = "email";
}

public interface IPublicAddress
{
    Task<PublicAddressDto> GetAsync(CancellationToken cancellationToken = default);

    Task<PublicAddressDto> SaveAsync(string? url, CancellationToken cancellationToken = default);
}

internal sealed class PublicAddressStore(
    KnowledgeDbContext db,
    DbContextGate gate,
    IConfiguration configuration,
    IIntegrationSettings integrations,
    TimeProvider timeProvider) : IPublicAddress
{
    public const string Key = "public-address";
    public const string ConfigurationKey = "App:PublicUrl";
    public const int MaxLength = 300;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<PublicAddressDto> GetAsync(CancellationToken cancellationToken = default)
    {
        string? json;
        using (await gate.EnterAsync(cancellationToken))
        {
            json = await SettingsDocuments.ReadAsync(db, Key, cancellationToken);
        }

        if (json is not null && Valid(JsonSerializer.Deserialize<Stored>(json, JsonOptions)?.Url) is { } saved)
        {
            return new PublicAddressDto(saved, PublicAddressSources.Saved);
        }

        if (Valid(configuration[ConfigurationKey]) is { } configured)
        {
            return new PublicAddressDto(configured, PublicAddressSources.Configuration);
        }

        return Valid((await integrations.GetEmailAsync(cancellationToken)).PortalUrl) is { } email
            ? new PublicAddressDto(email, PublicAddressSources.Email)
            : new PublicAddressDto(null, null);
    }

    public async Task<PublicAddressDto> SaveAsync(string? url, CancellationToken cancellationToken = default)
    {
        if (!TryNormalize(url, out var normalized, out var error))
        {
            throw new ValidationFailedException("url", error);
        }

        var json = normalized is null ? null : JsonSerializer.Serialize(new Stored(normalized), JsonOptions);
        using (await gate.EnterAsync(cancellationToken))
        {
            await SettingsDocuments.WriteAsync(db, Key, json, timeProvider.GetUtcNow(), cancellationToken);
        }

        return await GetAsync(cancellationToken);
    }

    public static bool TryNormalize(string? value, out string? url, out string error)
    {
        url = null;
        error = string.Empty;
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return true;
        }

        if (text.Length > MaxLength)
        {
            error = $"Use at most {MaxLength} characters.";
            return false;
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
            || string.IsNullOrEmpty(uri.Host)
            || !text.Contains("://", StringComparison.Ordinal))
        {
            error = "Enter a full address that starts with https://, such as https://support.example.com.";
            return false;
        }

        if (uri.UserInfo.Length > 0)
        {
            error = "Leave out the user name and password.";
            return false;
        }

        if (text.Contains('?', StringComparison.Ordinal) || text.Contains('#', StringComparison.Ordinal))
        {
            error = "Leave out the part after ? or #. Only a path such as /support may follow the domain.";
            return false;
        }

        url = uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
        return true;
    }

    private static string? Valid(string? value) => TryNormalize(value, out var url, out _) ? url : null;

    private sealed record Stored(string? Url);
}
