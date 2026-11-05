using System.Collections.Concurrent;
using System.Net;
using System.Text;

namespace KnowledgeAgent.Tests.Features.Sources.Support;

// Stands in for the network: answers by absolute URL and records every request it receives.
internal sealed class FakeWebHandler : HttpMessageHandler
{
    private readonly ConcurrentDictionary<string, Func<HttpRequestMessage, HttpResponseMessage>> _routes = new(StringComparer.Ordinal);

    public ConcurrentQueue<Uri> Requests { get; } = new();

    public FakeWebHandler Html(string url, string html) =>
        Map(url, _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(html, Encoding.UTF8, "text/html") });

    public FakeWebHandler Redirect(string url, string location) =>
        Map(url, _ => new HttpResponseMessage(HttpStatusCode.Found) { Headers = { Location = new Uri(location, UriKind.RelativeOrAbsolute) } });

    public FakeWebHandler Map(string url, Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        _routes[url] = respond;
        return this;
    }

    public bool WasRequested(string host) => Requests.Any(uri => uri.Host.Equals(host, StringComparison.OrdinalIgnoreCase));

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Enqueue(request.RequestUri!);
        return Task.FromResult(_routes.TryGetValue(request.RequestUri!.AbsoluteUri, out var respond)
            ? respond(request)
            : new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}
