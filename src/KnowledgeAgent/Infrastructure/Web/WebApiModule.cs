using System.Diagnostics;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace KnowledgeAgent.Infrastructure.Web;

public static class WebApiModule
{
    public static IHostApplicationBuilder AddWebApi(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var services = builder.Services;

        services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
        {
            context.ProblemDetails.Instance ??= context.HttpContext.Request.Path;
            context.ProblemDetails.Extensions.TryAdd("traceId", Activity.Current?.Id ?? context.HttpContext.TraceIdentifier);
        });
        services.AddExceptionHandler<ApiExceptionHandler>();
        services.ConfigureHttpJsonOptions(options => ApiJson.Configure(options.SerializerOptions));

        // Throwing lets the exception handler answer malformed bodies and parameters with a descriptive problem.
        services.PostConfigure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);

        return builder;
    }
}
