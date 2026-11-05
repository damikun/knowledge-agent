using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KnowledgeAgent.Infrastructure.Security;

public sealed record CurrentUserDto(string Name, string? Email, IReadOnlyList<string> Roles, string Mode)
{
    // The Keycloak subject: assignments and read state refer to it.
    public string? Id { get; init; }

    public bool CanManageUsers { get; init; }
}

internal static partial class AuthEndpoints
{
    public static RouteGroupBuilder MapAuthEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/auth").WithTags("Auth").AllowAnonymous();

        group.MapGet("/login", Login)
            .WithName("SignIn")
            .WithSummary("Starts the sign-in with the identity provider and returns to returnUrl, a path in the portal.");
        group.MapGet("/logout", Logout)
            .WithName("SignOut")
            .WithSummary("Ends the session here and at the identity provider, then returns to the portal.");
        group.MapGet("/me", Me)
            .WithName("GetCurrentUser")
            .WithSummary("The signed-in user, or 401.");

        return group;
    }

    private static IResult Login(string? returnUrl, IOptions<AuthOptions> options)
    {
        var target = SafeReturnUrl(returnUrl);
        return options.Value.Mode == AuthMode.None
            ? Results.LocalRedirect(target)
            : Results.Challenge(new AuthenticationProperties { RedirectUri = target }, [AuthSchemes.Oidc]);
    }

    private static IResult Logout(IOptions<AuthOptions> options) =>
        options.Value.Mode == AuthMode.None
            ? Results.LocalRedirect("/")
            : Results.SignOut(new AuthenticationProperties { RedirectUri = "/" }, [AuthSchemes.Cookie, AuthSchemes.Oidc]);

    private static async Task<Results<Ok<CurrentUserDto>, ProblemHttpResult>> Me(
        ClaimsPrincipal user,
        IOptions<AuthOptions> options,
        IEnumerable<ISignedInUserHook> hooks,
        ILoggerFactory loggers,
        CancellationToken cancellationToken)
    {
        if (user.Identity?.IsAuthenticated != true)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Not signed in", detail: "Sign in to use the portal.");
        }

        foreach (var hook in hooks)
        {
            // Bookkeeping only: the portal still learns who is signed in when it fails.
            try
            {
                await hook.SeenAsync(user, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogHookFailed(loggers.CreateLogger(typeof(AuthEndpoints)), ex);
            }
        }

        var name = user.FindFirst("name")?.Value ?? user.FindFirst("preferred_username")?.Value ?? user.FindFirst("email")?.Value ?? "Unknown user";
        var roles = user.FindAll("roles").Select(claim => claim.Value)
            .Where(role => role is AuthRoles.Admin or AuthRoles.Support)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
        var mode = options.Value.Mode == AuthMode.None ? "none" : "oidc";
        return TypedResults.Ok(new CurrentUserDto(name, user.FindFirst("email")?.Value, roles, mode)
        {
            Id = user.FindFirst("sub")?.Value,
            CanManageUsers = UserAdminClaim.IsUserAdmin(user),
        });
    }

    // Only paths in this site are accepted, so a crafted link cannot send a signed-in user elsewhere.
    internal static string SafeReturnUrl(string? returnUrl) =>
        returnUrl is { Length: > 0 } url && url[0] == '/' && !url.StartsWith("//", StringComparison.Ordinal) && !url.StartsWith("/\\", StringComparison.Ordinal)
            ? url
            : "/";

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not record the signed-in user")]
    private static partial void LogHookFailed(ILogger logger, Exception exception);
}
