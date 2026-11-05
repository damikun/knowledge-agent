using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace KnowledgeAgent.Infrastructure.Security;

public static class AuthorizationConventions
{
    // Reading stays open to staff; anything that changes data needs the admin role.
    public static TBuilder RequireAdminForChanges<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.Add(endpoint =>
        {
            var methods = endpoint.Metadata.OfType<IHttpMethodMetadata>().SelectMany(metadata => metadata.HttpMethods);
            if (methods.Any(method => !HttpMethods.IsGet(method) && !HttpMethods.IsHead(method)))
            {
                endpoint.Metadata.Add(new AuthorizeAttribute(AuthPolicies.Admin));
            }
        });
        return builder;
    }
}
