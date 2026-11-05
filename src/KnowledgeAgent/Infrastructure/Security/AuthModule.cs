using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace KnowledgeAgent.Infrastructure.Security;

public static class AuthModule
{
    public const string CookieName = "ka_session";
    public const string RequestHeader = "X-KA-Request";
    public const string LoginPath = "/api/auth/login";
    public const string CallbackPath = "/api/auth/callback";
    public const string SignedOutCallbackPath = "/api/auth/signed-out";

    // Matches the realm's 8 hour login timeout; with the default 15 minutes a sign-in page left open would fail on return.
    private static readonly TimeSpan SignInTimeout = TimeSpan.FromHours(8);

    // Marks a sign-in that started over once, so a failure that repeats ends on the portal's error screen.
    private const string RetriedItem = "ka.retried";

    public static IHostApplicationBuilder AddAuth(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var section = builder.Configuration.GetSection(AuthOptions.SectionName);
        var auth = section.Get<AuthOptions>() ?? new AuthOptions();
        Validate(auth, builder.Environment);
        builder.Services.Configure<AuthOptions>(section);

        if (auth.Mode == AuthMode.None)
        {
            builder.Services.AddAuthentication(AuthSchemes.Local)
                .AddScheme<AuthenticationSchemeOptions, LocalUserAuthenticationHandler>(AuthSchemes.Local, configureOptions: null);
        }
        else
        {
            AddOpenIdConnect(builder.Services, auth);
        }

        builder.Services.AddAuthorizationBuilder()
            .AddPolicy(AuthPolicies.Staff, policy => policy.RequireRole(AuthRoles.Admin, AuthRoles.Support))
            .AddPolicy(AuthPolicies.Admin, policy => policy.RequireRole(AuthRoles.Admin));

        return builder;
    }

    public static WebApplication UseAuth(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseMiddleware<RequestHeaderCsrfMiddleware>();
        return app;
    }

    // A wrong setting fails at startup with the fix, rather than at the first sign-in.
    private static void Validate(AuthOptions auth, IHostEnvironment environment)
    {
        if (auth.Mode == AuthMode.None)
        {
            if (!environment.IsDevelopment() && !environment.IsEnvironment("Testing"))
            {
                throw new InvalidOperationException(
                    $"Auth:Mode=None turns sign-in off and is only allowed in Development, not in {environment.EnvironmentName}. Configure Auth:Authority instead.");
            }

            return;
        }

        if (!Uri.TryCreate(auth.Authority, UriKind.Absolute, out _))
        {
            throw new InvalidOperationException(
                "Sign-in needs Auth:Authority (the Keycloak realm URL, e.g. http://localhost:8180/realms/knowledge-agent). "
                + "Aspire and Docker Compose set it; for local development without Keycloak use Auth:Mode=None.");
        }

        if (string.IsNullOrWhiteSpace(auth.ClientSecret))
        {
            throw new InvalidOperationException("Sign-in needs Auth:ClientSecret, the secret of the Keycloak client " + auth.ClientId + ".");
        }

        if (!string.IsNullOrWhiteSpace(auth.PublicOrigin) && !Uri.TryCreate(auth.PublicOrigin, UriKind.Absolute, out _))
        {
            throw new InvalidOperationException("Auth:PublicOrigin must be an absolute URL such as http://localhost:5173.");
        }

        if (auth.SessionCheckInterval <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("Auth:SessionCheckInterval must be a positive time such as 00:05:00.");
        }
    }

    private static void AddOpenIdConnect(IServiceCollection services, AuthOptions auth)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<SessionCheck>();
        services.AddAuthentication(options =>
            {
                options.DefaultScheme = AuthSchemes.Cookie;

                // The cookie challenge answers API calls with 401 instead of sending them to Keycloak.
                options.DefaultChallengeScheme = AuthSchemes.Cookie;
            })
            .AddCookie(AuthSchemes.Cookie, cookie =>
            {
                cookie.Cookie.Name = CookieName;
                cookie.Cookie.HttpOnly = true;
                cookie.Cookie.SameSite = SameSiteMode.Lax;
                cookie.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                cookie.ExpireTimeSpan = auth.SessionLifetime;
                cookie.SlidingExpiration = true;
                cookie.LoginPath = LoginPath;
                cookie.Events.OnRedirectToLogin = context => RedirectOrStatusAsync(context, StatusCodes.Status401Unauthorized);
                cookie.Events.OnRedirectToAccessDenied = context => RedirectOrStatusAsync(context, StatusCodes.Status403Forbidden);
                cookie.Events.OnValidatePrincipal = context => context.HttpContext.RequestServices.GetRequiredService<SessionCheck>().ValidateAsync(context);
            })
            .AddOpenIdConnect(AuthSchemes.Oidc, oidc =>
            {
                oidc.SignInScheme = AuthSchemes.Cookie;
                oidc.Authority = auth.Authority;
                if (!string.IsNullOrWhiteSpace(auth.MetadataAddress))
                {
                    oidc.MetadataAddress = auth.MetadataAddress;
                }

                oidc.RequireHttpsMetadata = auth.RequireHttpsMetadata;
                oidc.ClientId = auth.ClientId;
                oidc.ClientSecret = auth.ClientSecret;
                oidc.ResponseType = OpenIdConnectResponseType.Code;
                oidc.UsePkce = true;

                // The code comes back on a top-level GET, so Lax cookies carry the state check. The default form post
                // would need SameSite=None cookies, which browsers drop on plain http such as http://localhost.
                oidc.ResponseMode = OpenIdConnectResponseMode.Query;
                oidc.CorrelationCookie.SameSite = SameSiteMode.Lax;
                oidc.NonceCookie.SameSite = SameSiteMode.Lax;

                oidc.CorrelationCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                oidc.NonceCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;

                // The state check and the nonce stay valid as long as the sign-in page does.
                oidc.RemoteAuthenticationTimeout = SignInTimeout;
                oidc.ProtocolValidator.NonceLifetime = SignInTimeout;
                oidc.MapInboundClaims = false;
                oidc.GetClaimsFromUserInfoEndpoint = false;
                oidc.CallbackPath = CallbackPath;
                oidc.SignedOutCallbackPath = SignedOutCallbackPath;
                oidc.Scope.Clear();
                oidc.Scope.Add("openid");
                oidc.Scope.Add("profile");
                oidc.Scope.Add("email");
                oidc.TokenValidationParameters.NameClaimType = "name";
                oidc.TokenValidationParameters.RoleClaimType = "roles";
                oidc.TokenValidationParameters.ValidIssuer = auth.Authority!.TrimEnd('/');

                // Turns the realm-management roles of the ID token into ka_user_admin, at sign-in and at each check.
                oidc.ClaimActions.Add(new UserAdminClaim());

                // The session keeps the ID token and the refresh token; see SessionCheck.Start.
                oidc.SaveTokens = true;
                oidc.Events.OnTokenValidated = context =>
                {
                    if (string.IsNullOrEmpty(context.TokenEndpointResponse?.RefreshToken))
                    {
                        context.Fail("The identity provider issued no refresh token, which the session check needs. In Keycloak, turn on \"Use refresh tokens\" for the client.");
                    }

                    return Task.CompletedTask;
                };
                oidc.Events.OnTicketReceived = context =>
                {
                    context.HttpContext.RequestServices.GetRequiredService<SessionCheck>().Start(context.Properties!);
                    return Task.CompletedTask;
                };

                oidc.Events.OnRedirectToIdentityProvider = context =>
                {
                    if (PublicUrl(auth, CallbackPath) is { } callback)
                    {
                        // The handler keeps this address with the state and redeems the code with it.
                        context.ProtocolMessage.RedirectUri = callback;
                    }

                    return Task.CompletedTask;
                };
                oidc.Events.OnRedirectToIdentityProviderForSignOut = context =>
                {
                    if (PublicUrl(auth, SignedOutCallbackPath) is { } signedOut)
                    {
                        context.ProtocolMessage.PostLogoutRedirectUri = signedOut;
                    }

                    return Task.CompletedTask;
                };
                oidc.Events.OnRemoteFailure = async context =>
                {
                    context.HandleResponse();
                    if (RetryOf(context) is { } retry)
                    {
                        await context.HttpContext.ChallengeAsync(AuthSchemes.Oidc, retry);
                        return;
                    }

                    context.Response.Redirect("/?authError=" + Uri.EscapeDataString(context.Failure?.Message ?? "Sign-in failed."));
                };
            });
    }

    private static string? PublicUrl(AuthOptions auth, string path) =>
        string.IsNullOrWhiteSpace(auth.PublicOrigin) ? null : auth.PublicOrigin.TrimEnd('/') + path;

    // Keycloak signed the user in but our side went stale (expired correlation cookie, callback reopened from history).
    // One retry passes Keycloak's session without the password; errors Keycloak reports would only repeat.
    private static AuthenticationProperties? RetryOf(RemoteFailureContext context)
    {
        if (context.Properties is not { } failed
            || failed.Items.ContainsKey(RetriedItem)
            || context.Request.Query.ContainsKey(OpenIdConnectParameterNames.Error))
        {
            return null;
        }

        var retry = new AuthenticationProperties { RedirectUri = AuthEndpoints.SafeReturnUrl(failed.RedirectUri) };
        retry.Items[RetriedItem] = "1";
        return retry;
    }

    private static Task RedirectOrStatusAsync(RedirectContext<CookieAuthenticationOptions> context, int statusCode)
    {
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            context.Response.StatusCode = statusCode;
        }
        else
        {
            // Pages outside the API, such as the API reference, send the browser to sign in first.
            context.Response.Redirect(context.RedirectUri);
        }

        return Task.CompletedTask;
    }
}
