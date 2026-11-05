using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KnowledgeAgent.Api.Tests.Support;

// Lets a scenario act as a user with given roles: X-Test-Roles lists them, and no header means not signed in.
internal static class TestUsers
{
    public const string Scheme = "test";
    public const string RolesHeader = "X-Test-Roles";

    public static void Install(IServiceCollection services)
    {
        services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, Handler>(Scheme, configureOptions: null);
        services.PostConfigure<AuthenticationOptions>(options =>
        {
            options.DefaultScheme = Scheme;
            options.DefaultAuthenticateScheme = Scheme;
            options.DefaultChallengeScheme = Scheme;
            options.DefaultForbidScheme = Scheme;
        });
    }

    public static HttpRequestMessage As(this HttpRequestMessage request, params string[] roles)
    {
        request.Headers.Add(RolesHeader, string.Join(',', roles));
        return request;
    }

    private sealed class Handler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(RolesHeader, out var header))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var claims = new List<Claim> { new("name", "Test user"), new("email", "test@example.com") };
            claims.AddRange(header.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(role => new Claim("roles", role)));
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, TestUsers.Scheme, "name", "roles"));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, TestUsers.Scheme)));
        }
    }
}
