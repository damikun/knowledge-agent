using Microsoft.AspNetCore.Http;

namespace KnowledgeAgent.Infrastructure.Security;

// Cookie-authenticated changes must carry a header that a cross-site page cannot set without a CORS preflight.
// Requests without the cookie (Inngest, widgets, tests) are not affected.
internal sealed class RequestHeaderCsrfMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var request = context.Request;
        if (IsUnsafe(request.Method)
            && request.Cookies.ContainsKey(AuthModule.CookieName)
            && !request.Headers.ContainsKey(AuthModule.RequestHeader))
        {
            await Results.Problem(
                    statusCode: StatusCodes.Status403Forbidden,
                    title: "Missing request header",
                    detail: $"Requests that change data must send the {AuthModule.RequestHeader} header.")
                .ExecuteAsync(context);
            return;
        }

        await next(context);
    }

    private static bool IsUnsafe(string method) =>
        !(HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method) || HttpMethods.IsTrace(method));
}
