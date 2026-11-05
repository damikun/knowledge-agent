using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace KnowledgeAgent.Infrastructure.Security;

// Re-checks each session every Auth:SessionCheckInterval by redeeming its refresh token.
// While the provider is unreachable the session stays as it was and is retried a minute later.
internal sealed partial class SessionCheck(
    IOptionsMonitor<OpenIdConnectOptions> oidcOptions,
    IOptions<AuthOptions> authOptions,
    TimeProvider timeProvider,
    ILogger<SessionCheck> logger)
{
    private const string IdToken = "id_token";
    private const string RefreshToken = "refresh_token";

    internal const string DueItem = "ka.check-due";

    private static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(1);

    // Long enough for a slow token endpoint, short enough that requests do not hang on an unreachable one.
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    private readonly ConcurrentDictionary<string, Lazy<Task<Outcome>>> _checks = new(StringComparer.Ordinal);

    // When the session of this request is due for a check; null when it is not checked, as with Auth:Mode=None.
    public static DateTimeOffset? DueAt(HttpContext context) =>
        context.Features.Get<IAuthenticateResultFeature>()?.AuthenticateResult?.Properties is { } properties ? DueAt(properties) : null;

    // Keycloak needs the ID token as the logout hint and the check redeems the refresh token.
    // The access token is dropped; it would only grow the cookie.
    public void Start(AuthenticationProperties properties)
    {
        properties.StoreTokens(Tokens(properties.GetTokenValue(IdToken), properties.GetTokenValue(RefreshToken)));
        Schedule(properties, authOptions.Value.SessionCheckInterval);
    }

    public async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        if (DueAt(context.Properties) is { } due && timeProvider.GetUtcNow() < due)
        {
            return;
        }

        var principal = context.Principal!;
        Outcome outcome;
        if (context.Properties.GetTokenValue(RefreshToken) is { Length: > 0 } refreshToken)
        {
            // Concurrent requests of one browser share one check, so Keycloak is asked once.
            var check = _checks.GetOrAdd(refreshToken, token => new Lazy<Task<Outcome>>(() => CheckAsync(token, principal)));
            outcome = await check.Value.WaitAsync(context.HttpContext.RequestAborted);
        }
        else
        {
            outcome = new Ended("the session holds no refresh token");
            Log(outcome, principal);
        }

        switch (outcome)
        {
            case Confirmed confirmed:
                // A copy, as requests that shared the check must not share a principal.
                context.ReplacePrincipal(confirmed.Principal.Clone());
                context.Properties.StoreTokens(Tokens(confirmed.IdToken, confirmed.RefreshToken));
                Schedule(context.Properties, authOptions.Value.SessionCheckInterval);
                context.ShouldRenew = true;
                break;
            case Ended:
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync(context.Scheme.Name);
                break;
            case Unreachable:
                var interval = authOptions.Value.SessionCheckInterval;
                Schedule(context.Properties, interval < RetryAfter ? interval : RetryAfter);
                context.ShouldRenew = true;
                break;
        }
    }

    private async Task<Outcome> CheckAsync(string refreshToken, ClaimsPrincipal principal)
    {
        try
        {
            var outcome = await RefreshAsync(refreshToken, principal.FindFirst("sub")?.Value);
            Log(outcome, principal);
            return outcome;
        }
        finally
        {
            _checks.TryRemove(refreshToken, out _);
        }
    }

    private void Log(Outcome outcome, ClaimsPrincipal principal)
    {
        var subject = principal.FindFirst("sub")?.Value ?? "unknown";
        switch (outcome)
        {
            case Confirmed confirmed:
                var before = RoleList(principal);
                var after = RoleList(confirmed.Principal);
                if (before != after)
                {
                    LogRolesChanged(logger, subject, before, after);
                }

                break;
            case Ended ended:
                LogEnded(logger, subject, ended.Reason);
                break;
            case Unreachable unreachable:
                LogUnreachable(logger, subject, unreachable.Reason);
                break;
        }
    }

    private async Task<Outcome> RefreshAsync(string refreshToken, string? subject)
    {
        var options = oidcOptions.Get(AuthSchemes.Oidc);
        using var timeout = new CancellationTokenSource(RequestTimeout);
        OpenIdConnectConfiguration configuration;
        HttpStatusCode status;
        string body;
        try
        {
            configuration = await options.ConfigurationManager!.GetConfigurationAsync(timeout.Token);
            using var request = new HttpRequestMessage(HttpMethod.Post, configuration.TokenEndpoint);
            request.Content = new FormUrlEncodedContent(
            [
                new(OpenIdConnectParameterNames.GrantType, OpenIdConnectGrantTypes.RefreshToken),
                new(OpenIdConnectParameterNames.RefreshToken, refreshToken),
                new(OpenIdConnectParameterNames.ClientId, options.ClientId),
                new(OpenIdConnectParameterNames.ClientSecret, options.ClientSecret),
            ]);
            using var response = await options.Backchannel.SendAsync(request, timeout.Token);
            status = response.StatusCode;
            body = await response.Content.ReadAsStringAsync(timeout.Token);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or InvalidOperationException)
        {
            // InvalidOperationException is how the configuration manager reports metadata it could not fetch.
            return new Unreachable(ex.Message);
        }

        if ((int)status >= 500 || status is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests)
        {
            return new Unreachable($"the token endpoint answered {(int)status}");
        }

        // Keycloak answers invalid_grant for a disabled account and for a session that ended there.
        var answer = Parse(body);
        if (status != HttpStatusCode.OK)
        {
            return new Ended(answer.Error is { } error ? $"{error}: {answer.ErrorDescription}" : $"the token endpoint answered {(int)status}");
        }

        if (string.IsNullOrEmpty(answer.IdToken))
        {
            return new Ended("the token endpoint returned no ID token");
        }

        var parameters = options.TokenValidationParameters.Clone();
        if (options.ConfigurationManager is BaseConfigurationManager manager)
        {
            parameters.ConfigurationManager = manager;
        }
        else
        {
            parameters.ValidIssuers = [.. parameters.ValidIssuers ?? [], configuration.Issuer];
            parameters.IssuerSigningKeys = configuration.SigningKeys;
        }

        var validated = await options.TokenHandler.ValidateTokenAsync(answer.IdToken, parameters);
        if (!validated.IsValid)
        {
            return new Ended("the new ID token is not valid: " + validated.Exception?.Message);
        }

        var identity = validated.ClaimsIdentity;
        if (!string.Equals(identity.FindFirst("sub")?.Value, subject, StringComparison.Ordinal))
        {
            return new Ended("the new ID token belongs to another user");
        }

        // Sign-in drops protocol claims such as aud, exp and nonce from the principal, and so does the check.
        using (var empty = JsonDocument.Parse("{}"))
        {
            foreach (var action in options.ClaimActions)
            {
                action.Run(empty.RootElement, identity, options.ClaimsIssuer ?? AuthSchemes.Oidc);
            }
        }

        return new Confirmed(new ClaimsPrincipal(identity), answer.IdToken, answer.RefreshToken ?? refreshToken);
    }

    private static TokenAnswer Parse(string body)
    {
        try
        {
            using var json = JsonDocument.Parse(body);
            var root = json.RootElement;
            return new TokenAnswer(Read(root, IdToken), Read(root, RefreshToken), Read(root, "error"), Read(root, "error_description"));
        }
        catch (JsonException)
        {
            return new TokenAnswer(null, null, null, null);
        }

        static string? Read(JsonElement root, string name) =>
            root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }

    private static DateTimeOffset? DueAt(AuthenticationProperties properties) =>
        properties.Items.TryGetValue(DueItem, out var value)
        && DateTimeOffset.TryParseExact(value, "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out var due)
            ? due
            : null;

    private void Schedule(AuthenticationProperties properties, TimeSpan after) =>
        properties.Items[DueItem] = (timeProvider.GetUtcNow() + after).ToString("O", CultureInfo.InvariantCulture);

    private static List<AuthenticationToken> Tokens(string? idToken, string? refreshToken)
    {
        var tokens = new List<AuthenticationToken>(2);
        if (!string.IsNullOrEmpty(idToken))
        {
            tokens.Add(new AuthenticationToken { Name = IdToken, Value = idToken });
        }

        if (!string.IsNullOrEmpty(refreshToken))
        {
            tokens.Add(new AuthenticationToken { Name = RefreshToken, Value = refreshToken });
        }

        return tokens;
    }

    private static string RoleList(ClaimsPrincipal principal) =>
        string.Join(", ", principal.FindAll("roles").Select(claim => claim.Value).Order(StringComparer.Ordinal));

    [LoggerMessage(Level = LogLevel.Information, Message = "The session of {Subject} ended at its check: {Reason}")]
    private static partial void LogEnded(ILogger logger, string subject, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not check the session of {Subject} with the identity provider, so it stays as it was for now: {Reason}")]
    private static partial void LogUnreachable(ILogger logger, string subject, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "The roles of {Subject} changed from [{Before}] to [{After}]")]
    private static partial void LogRolesChanged(ILogger logger, string subject, string before, string after);

    private abstract record Outcome;

    private sealed record Confirmed(ClaimsPrincipal Principal, string IdToken, string RefreshToken) : Outcome;

    private sealed record Ended(string Reason) : Outcome;

    private sealed record Unreachable(string Reason) : Outcome;

    private sealed record TokenAnswer(string? IdToken, string? RefreshToken, string? Error, string? ErrorDescription);
}
