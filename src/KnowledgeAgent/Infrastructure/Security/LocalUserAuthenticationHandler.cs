using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KnowledgeAgent.Infrastructure.Security;

// Auth:Mode=None: every request is the same local admin, so the in-memory profile and the tests need no identity provider.
internal sealed class LocalUserAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string UserName = "Local admin";

    private static readonly ClaimsPrincipal LocalAdmin = new(new ClaimsIdentity(
        [
            new Claim("sub", "local-admin"),
            new Claim("name", UserName),
            new Claim("roles", AuthRoles.Admin),
            new Claim("roles", AuthRoles.Support),
        ],
        AuthSchemes.Local,
        nameType: "name",
        roleType: "roles"));

    protected override Task<AuthenticateResult> HandleAuthenticateAsync() =>
        Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(LocalAdmin, AuthSchemes.Local)));
}
