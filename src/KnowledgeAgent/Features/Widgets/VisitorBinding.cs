using KnowledgeAgent.Features.Chat;
using KnowledgeAgent.Features.Handover;
using KnowledgeAgent.Infrastructure.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeAgent.Features.Widgets;

public sealed class WidgetNotFoundException() : Exception("This chat is not available.");

public sealed class VisitorSessionExpiredException() : Exception("Start a new visitor session.");

public sealed partial record VisitorContext
{
    public static async ValueTask<VisitorContext?> BindAsync(HttpContext http)
    {
        var visitors = http.RequestServices.GetRequiredService<VisitorService>();
        var key = http.GetRouteValue("key") as string ?? string.Empty;
        var token = PublicWidgetEndpoints.BearerToken(http.Request);
        var widget = await visitors.FindWidgetAsync(key, token, http.RequestAborted) ?? throw new WidgetNotFoundException();
        return await visitors.AuthenticateAsync(widget, token, http.RequestAborted, http.User.IsInRole(AuthRoles.Admin))
            ?? throw new VisitorSessionExpiredException();
    }
}

public sealed record VisitorConversation(VisitorContext Visitor, Conversation Conversation)
{
    public Guid Id => Conversation.Id;

    public static async ValueTask<VisitorConversation?> BindAsync(HttpContext http)
    {
        var visitor = (await VisitorContext.BindAsync(http))!;
        var id = Guid.Parse(http.GetRouteValue("id") as string ?? string.Empty);
        var conversation = await http.RequestServices.GetRequiredService<VisitorService>()
            .FindOwnConversationAsync(visitor, id, includeTurns: false, http.RequestAborted)
            ?? throw new ConversationNotFoundException(id);
        return new VisitorConversation(visitor, conversation);
    }
}
