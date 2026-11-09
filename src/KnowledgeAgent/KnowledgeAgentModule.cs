using KnowledgeAgent.Features.Agents;
using KnowledgeAgent.Features.Agents.Access;
using KnowledgeAgent.Features.Alerts;
using KnowledgeAgent.Features.Analytics;
using KnowledgeAgent.Features.Chat;
using KnowledgeAgent.Features.Connections;
using KnowledgeAgent.Features.Connections.Mcp;
using KnowledgeAgent.Features.Conversations;
using KnowledgeAgent.Features.Evaluations;
using KnowledgeAgent.Features.Feedback;
using KnowledgeAgent.Features.Handover;
using KnowledgeAgent.Features.Help;
using KnowledgeAgent.Features.Integrations;
using KnowledgeAgent.Features.Jobs;
using KnowledgeAgent.Features.Labels;
using KnowledgeAgent.Features.Models;
using KnowledgeAgent.Features.Overview;
using KnowledgeAgent.Features.People;
using KnowledgeAgent.Features.Prompts;
using KnowledgeAgent.Features.Search;
using KnowledgeAgent.Features.Sources;
using KnowledgeAgent.Features.Staff;
using KnowledgeAgent.Features.SystemInfo;
using KnowledgeAgent.Features.Widgets;
using KnowledgeAgent.Infrastructure.AI;
using KnowledgeAgent.Infrastructure.Jobs;
using KnowledgeAgent.Infrastructure.Persistence;
using KnowledgeAgent.Infrastructure.Security;
using KnowledgeAgent.Infrastructure.VectorStore;
using KnowledgeAgent.Infrastructure.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace KnowledgeAgent;

public static class KnowledgeAgentModule
{
    public static IHostApplicationBuilder AddKnowledgeAgent(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddPersistence();
        builder.AddJobs();
        builder.AddVectorStore();
        builder.AddPrompts();
        builder.AddAi();
        builder.AddSources();
        builder.AddAgents();
        builder.AddAgentAccess();
        builder.AddConnections();
        builder.AddSearch();
        builder.AddChat();
        builder.AddConversations();
        builder.AddLabels();
        builder.AddPeople();
        builder.AddStaff();
        builder.AddWidgets();
        builder.AddFeedback();
        builder.AddIntegrations();
        builder.AddHandover();
        builder.AddOverview();
        builder.AddAnalytics();
        builder.AddModels();
        builder.AddEvaluations();
        builder.AddAlerts();
        builder.AddHelp();
        builder.AddWebApi();
        builder.Services.TryAddSingleton<SystemHealthService>();

        return builder;
    }

    public static RouteGroupBuilder MapKnowledgeAgentApi(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var configuration = endpoints.ServiceProvider.GetRequiredService<IConfiguration>();
        // Everything needs a signed-in staff member unless it says otherwise; configuration needs the admin role.
        var api = endpoints.MapGroup("/api").RequireAuthorization(AuthPolicies.Staff);

        api.MapAuthEndpoints();
        api.MapSourceEndpoints(SourceEndpoints.UploadRequestLimit(configuration)).RequireAdminForChanges();
        api.MapSearchEndpoints();
        api.MapChatEndpoints();
        api.MapLinkHostEndpoints();
        api.MapConversationEndpoints();
        api.MapConversationSearchEndpoints();
        api.MapConversationCountEndpoints();
        api.MapConversationReadEndpoints();
        api.MapStaffEndpoints();
        api.MapLabelEndpoints();
        api.MapPeopleEndpoints();
        api.MapSegmentEndpoints();
        api.MapModelEndpoints().RequireAdminForChanges();
        api.MapAgentEndpoints().RequireAdminForChanges();
        api.MapAgentAccessEndpoints().RequireAuthorization(AuthPolicies.Admin);
        api.MapMcpServerEndpoints().RequireAuthorization(AuthPolicies.Admin);
        api.MapWidgetEndpoints().RequireAuthorization(AuthPolicies.Admin);
        api.MapFeedbackEndpoints();
        api.MapHandoverEndpoints();
        api.MapReplySuggestionEndpoints();
        api.MapOverviewEndpoints().RequireAuthorization(AuthPolicies.Admin);
        api.MapAnalyticsEndpoints().RequireAuthorization(AuthPolicies.Admin);
        api.MapAlertEndpoints();
        api.MapHelpEndpoints();
        api.MapIntegrationEndpoints().RequireAuthorization(AuthPolicies.Admin);
        api.MapPromptEndpoints().RequireAuthorization(AuthPolicies.Admin);
        api.MapEvalEndpoints().RequireAuthorization(AuthPolicies.Admin);
        api.MapJobEndpoints().RequireAuthorization(AuthPolicies.Admin);
        if (endpoints.ServiceProvider.GetService<InngestJobEngine>() is not null)
        {
            // Inngest signs its requests, and the SDK checks the signature outside dev mode.
            api.MapInngest(InngestJobEngine.EndpointPath).ExcludeFromDescription().AllowAnonymous();
        }

        api.MapSystemEndpoints();
        api.MapPublicAddressEndpoints();
        api.MapSystemHealthEndpoint().RequireAuthorization(AuthPolicies.Admin);

        endpoints.MapPublicWidgetEndpoints();

        endpoints.MapAgentPublicEndpoints();
        endpoints.MapWidgetFrameEndpoints();

        return api;
    }
}
