using KnowledgeAgent.Features.Agents;
using KnowledgeAgent.Features.Integrations;
using KnowledgeAgent.Features.Sources;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace KnowledgeAgent.Features.Widgets;

public sealed record LinkHostsDto(IReadOnlyList<string> Hosts);

// Answers may only link to hosts the team chose: this installation, the sites a widget runs on and the websites in
// the knowledge base. Any other address is shown as plain text, so a planted link needs a person to copy it.
// "example.com" also allows www.example.com; "*.example.com" allows its subdomains.
internal sealed class LinkHosts(IPublicAddress publicAddress, IWidgetRepository widgets, ISourceRepository sources, IAgentResolver agents)
{
    public const int MaxSites = 200;

    public async Task<IReadOnlyList<string>> ForWidgetAsync(Widget widget, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(widget);
        var hosts = new SortedSet<string>(StringComparer.Ordinal);
        await AddPublicAddressAsync(hosts, cancellationToken);
        AddOrigins(hosts, widget.Settings.Security.AllowedOrigins);
        if (await agents.FindAsync(widget.AgentId, cancellationToken) is { IsBuiltIn: false } agent)
        {
            var scope = await agents.ResolveScopeAsync(agent, [], cancellationToken);
            if (scope is not { Count: 0 })
            {
                await AddSitesAsync(hosts, new SourceQuery(Ids: scope), cancellationToken);
            }
        }

        return [.. hosts];
    }

    // Staff read answers from every agent, so the portal allows the sites of every widget and source.
    public async Task<IReadOnlyList<string>> ForPortalAsync(CancellationToken cancellationToken)
    {
        var hosts = new SortedSet<string>(StringComparer.Ordinal);
        await AddPublicAddressAsync(hosts, cancellationToken);
        foreach (var widget in await widgets.ListAsync(cancellationToken))
        {
            AddOrigins(hosts, widget.Settings.Security.AllowedOrigins);
        }

        await AddSitesAsync(hosts, new SourceQuery(), cancellationToken);
        return [.. hosts];
    }

    internal static string? HostOf(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            ? SourceDefaults.NormalizeSite(uri.IdnHost)
            : null;

    private async Task AddPublicAddressAsync(SortedSet<string> hosts, CancellationToken cancellationToken)
    {
        if (HostOf((await publicAddress.GetAsync(cancellationToken)).Url) is { } host)
        {
            hosts.Add(host);
        }
    }

    private static void AddOrigins(SortedSet<string> hosts, IEnumerable<string> origins)
    {
        foreach (var origin in origins)
        {
            var schemeEnd = origin.IndexOf("://", StringComparison.Ordinal);
            var host = schemeEnd < 0 ? origin : origin[(schemeEnd + 3)..];
            var portStart = host.LastIndexOf(':');
            host = (portStart > 0 ? host[..portStart] : host).ToLowerInvariant();
            if (host.Length > 0)
            {
                hosts.Add(host.StartsWith("*.", StringComparison.Ordinal) ? host : SourceDefaults.NormalizeSite(host));
            }
        }
    }

    private async Task AddSitesAsync(SortedSet<string> hosts, SourceQuery query, CancellationToken cancellationToken)
    {
        foreach (var site in await sources.CountBySiteAsync(query, MaxSites, cancellationToken))
        {
            hosts.Add(site.Site);
        }
    }
}

internal static class LinkHostEndpoints
{
    public static RouteHandlerBuilder MapLinkHostEndpoints(this IEndpointRouteBuilder routes) =>
        routes.MapGet("/link-hosts", async (LinkHosts links, CancellationToken cancellationToken) =>
                TypedResults.Ok(new LinkHostsDto(await links.ForPortalAsync(cancellationToken))))
            .WithTags("Conversations")
            .WithName("GetLinkHosts")
            .WithSummary("Hosts that answers may link to; the portal shows links to other hosts as plain text.");
}
