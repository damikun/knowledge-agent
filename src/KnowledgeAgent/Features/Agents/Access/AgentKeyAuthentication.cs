using System.Globalization;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace KnowledgeAgent.Features.Agents.Access;

public static class AgentKeyAuth
{
    public const string Scheme = "agent-key";

    public const string McpPolicy = "agent-mcp";
    public const string ApiPolicy = "agent-api";

    public const string ChannelClaim = "ka_channel";
    public const string AgentClaim = "ka_agent";
    public const string KeyClaim = "ka_key";
    public const string RateClaim = "ka_rpm";
    public const string McpChannel = "mcp";
    public const string ApiChannel = "api";

    public const string AgentKeyRoute = "agentKey";

    public static Guid AgentId(ClaimsPrincipal user) => Guid.Parse(user.FindFirst(AgentClaim)!.Value, CultureInfo.InvariantCulture);

    public static Guid KeyId(ClaimsPrincipal user) => Guid.Parse(user.FindFirst(KeyClaim)!.Value, CultureInfo.InvariantCulture);

    public static int RequestsPerMinute(ClaimsPrincipal user) =>
        int.TryParse(user.FindFirst(RateClaim)?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rate) ? rate : AgentApiKey.DefaultRequestsPerMinute;
}

// Bearer ka_... A key only works for its own agent; enabled channels become claims the policies require.
internal sealed class AgentKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.NoResult();
        }

        var presented = header["Bearer ".Length..].Trim();
        var match = await Context.RequestServices.GetRequiredService<AgentKeyAuthenticator>().AuthenticateAsync(presented, Context.RequestAborted);
        if (match is null)
        {
            return AuthenticateResult.Fail("The API key is not valid or was revoked.");
        }

        if (Context.GetRouteValue(AgentKeyAuth.AgentKeyRoute) is not string agentKey
            || !string.Equals(agentKey, match.Agent.Key, StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.Fail("The API key belongs to another agent.");
        }

        var claims = new List<Claim>
        {
            new("sub", "apikey:" + match.Key.Id.ToString("N")),
            new("name", match.Key.Name),
            new(AgentKeyAuth.AgentClaim, match.Agent.Id.ToString("D")),
            new(AgentKeyAuth.KeyClaim, match.Key.Id.ToString("D")),
            new(AgentKeyAuth.RateClaim, match.Key.RequestsPerMinute.ToString(CultureInfo.InvariantCulture)),
        };
        if (match.Agent.McpServerEnabled)
        {
            claims.Add(new Claim(AgentKeyAuth.ChannelClaim, AgentKeyAuth.McpChannel));
        }

        if (match.Agent.HttpApiEnabled)
        {
            claims.Add(new Claim(AgentKeyAuth.ChannelClaim, AgentKeyAuth.ApiChannel));
        }

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, AgentKeyAuth.Scheme, "name", "roles"));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, AgentKeyAuth.Scheme));
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers[HeaderNames.WWWAuthenticate] = "Bearer";
        await Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "API key required",
                detail: "Send one of this agent's API keys as Authorization: Bearer <key>. Admins create keys on the agent's Use elsewhere tab.")
            .ExecuteAsync(Context);
    }

    protected override async Task HandleForbiddenAsync(AuthenticationProperties properties) =>
        await Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Turned off",
                detail: "This way of using the agent is turned off. Admins turn it on on the agent's Use elsewhere tab.")
            .ExecuteAsync(Context);
}
