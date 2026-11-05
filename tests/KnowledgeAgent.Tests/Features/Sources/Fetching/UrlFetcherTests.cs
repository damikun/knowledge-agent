using System.Text;
using KnowledgeAgent.Features.Sources.Fetching;
using KnowledgeAgent.Features.Sources.Ingestion;
using KnowledgeAgent.TestKit;
using KnowledgeAgent.Tests.Features.Sources.Support;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeAgent.Tests.Features.Sources.Fetching;

public sealed class UrlFetcherTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Loopback_servers_are_blocked_by_default()
    {
        await using var server = await TestHttpServer.StartAsync();
        server.AddHtml("page", "<html><body>secret intranet page</body></html>");
        await using var context = new KnowledgeTestContext(o => o.AllowPrivateNetworks = false);

        var ex = await Should.ThrowAsync<IngestionException>(() => Fetcher(context).FetchAsync(server.Url("page"), Ct));

        ex.IsPermanent.ShouldBeTrue();
        ex.Message.ShouldContain("not allowed");
        server.Hits("page").ShouldBe(0);
    }

    [Fact]
    public async Task Host_names_resolving_to_loopback_are_blocked()
    {
        await using var server = await TestHttpServer.StartAsync();
        server.AddHtml("page", "<html><body>local</body></html>");
        await using var context = new KnowledgeTestContext(o => o.AllowPrivateNetworks = false);
        var url = new UriBuilder(server.Url("page")) { Host = "localhost" }.Uri;

        var ex = await Should.ThrowAsync<IngestionException>(() => Fetcher(context).FetchAsync(url, Ct));

        ex.IsPermanent.ShouldBeTrue();
        server.Hits("page").ShouldBe(0);
    }

    [Fact]
    public async Task Private_networks_can_be_allowed()
    {
        await using var server = await TestHttpServer.StartAsync();
        server.AddHtml("page", "<html><body>hello intranet</body></html>");
        await using var context = new KnowledgeTestContext(o => o.AllowPrivateNetworks = true);

        var result = await Fetcher(context).FetchAsync(server.Url("page"), Ct);

        Encoding.UTF8.GetString(result.Content).ShouldBe("<html><body>hello intranet</body></html>");
        result.MediaType.ShouldBe("text/html");
        result.CharSet.ShouldBe("utf-8");
        result.FinalUri.ShouldBe(server.Url("page"));
        result.RequestedUri.ShouldBe(server.Url("page"));
        result.Redirects.ShouldBe(0);
        result.FileName.ShouldBe("/page");
    }

    [Fact]
    public async Task Redirects_are_followed_and_the_final_url_is_recorded()
    {
        await using var server = await ScriptedHttpServer.StartAsync();
        server.MapRedirect("start", "/middle")
            .MapRedirect("middle", server.Url("docs/final").AbsoluteUri, StatusCodes.Status301MovedPermanently)
            .MapText("docs/final", "final page");
        await using var context = new KnowledgeTestContext();

        var result = await Fetcher(context).FetchAsync(server.Url("start"), Ct);

        Encoding.UTF8.GetString(result.Content).ShouldBe("final page");
        result.FinalUri.ShouldBe(server.Url("docs/final"));
        result.RequestedUri.ShouldBe(server.Url("start"));
        result.Redirects.ShouldBe(2);
    }

    [Fact]
    public async Task Relative_redirect_locations_are_resolved_against_the_current_url()
    {
        await using var server = await ScriptedHttpServer.StartAsync();
        server.MapRedirect("docs/start", "next", StatusCodes.Status307TemporaryRedirect).MapText("docs/next", "next page");
        await using var context = new KnowledgeTestContext();

        var result = await Fetcher(context).FetchAsync(server.Url("docs/start"), Ct);

        result.FinalUri.ShouldBe(server.Url("docs/next"));
        Encoding.UTF8.GetString(result.Content).ShouldBe("next page");
    }

    [Fact]
    public async Task At_most_five_redirects_are_followed()
    {
        await using var server = await ScriptedHttpServer.StartAsync();
        for (var i = 0; i < 6; i++)
        {
            server.MapRedirect($"r{i}", $"/r{i + 1}");
        }

        server.MapText("r5", "five redirects deep");
        await using var context = new KnowledgeTestContext();

        var fiveHops = await Fetcher(context).FetchAsync(server.Url("r0"), Ct);
        fiveHops.Redirects.ShouldBe(5);

        server.MapRedirect("r5", "/r6").MapText("r6", "six redirects deep");
        var ex = await Should.ThrowAsync<IngestionException>(() => Fetcher(context).FetchAsync(server.Url("r0"), Ct));
        ex.Message.ShouldContain("Too many redirects");
        ex.IsPermanent.ShouldBeTrue();
        server.Hits("r6").ShouldBe(0);
    }

    [Theory]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://example.com/file.txt")]
    [InlineData("gopher://127.0.0.1:6379/_INFO")]
    public async Task Redirects_to_other_schemes_are_rejected(string location)
    {
        await using var server = await ScriptedHttpServer.StartAsync();
        server.MapRedirect("start", location);
        await using var context = new KnowledgeTestContext();

        var ex = await Should.ThrowAsync<IngestionException>(() => Fetcher(context).FetchAsync(server.Url("start"), Ct));

        ex.IsPermanent.ShouldBeTrue();
        ex.Message.ShouldContain("Only http and https URLs can be fetched");
    }

    [Fact]
    public async Task Redirect_without_location_fails()
    {
        await using var server = await ScriptedHttpServer.StartAsync();
        server.Map("start", context =>
        {
            context.Response.StatusCode = StatusCodes.Status302Found;
            return Task.CompletedTask;
        });
        await using var context = new KnowledgeTestContext();

        var ex = await Should.ThrowAsync<IngestionException>(() => Fetcher(context).FetchAsync(server.Url("start"), Ct));

        ex.Message.ShouldContain("Location");
    }

    [Fact]
    public async Task Non_http_urls_are_rejected_without_a_request()
    {
        await using var context = new KnowledgeTestContext();

        var ex = await Should.ThrowAsync<IngestionException>(() => Fetcher(context).FetchAsync(new Uri("ftp://example.com/file"), Ct));

        ex.IsPermanent.ShouldBeTrue();
    }

    [Fact]
    public async Task Bodies_larger_than_the_download_limit_are_rejected()
    {
        await using var server = await TestHttpServer.StartAsync();
        server.AddText("big.txt", new string('a', 64 * 1024));
        server.AddText("small.txt", new string('b', 1024));
        await using var context = new KnowledgeTestContext(o => o.MaxDownloadBytes = 16 * 1024);

        var ex = await Should.ThrowAsync<IngestionException>(() => Fetcher(context).FetchAsync(server.Url("big.txt"), Ct));
        ex.IsPermanent.ShouldBeTrue();
        ex.Message.ShouldContain("maximum download size of 16 KB");

        var small = await Fetcher(context).FetchAsync(server.Url("small.txt"), Ct);
        small.Content.Length.ShouldBe(1024);
    }

    [Fact]
    public async Task Declared_content_length_above_the_limit_is_rejected_before_reading()
    {
        await using var server = await ScriptedHttpServer.StartAsync();
        server.Map("huge", async http =>
        {
            http.Response.ContentType = "text/plain";
            http.Response.ContentLength = 10 * 1024 * 1024;
            try
            {
                await http.Response.Body.FlushAsync(http.RequestAborted);
                await Task.Delay(TimeSpan.FromSeconds(30), http.RequestAborted);
            }
            catch (Exception)
            {
                // The client hangs up without reading the body.
            }
        });
        await using var context = new KnowledgeTestContext(o => o.MaxDownloadBytes = 1024);

        var ex = await Should.ThrowAsync<IngestionException>(() => Fetcher(context).FetchAsync(server.Url("huge"), Ct));

        ex.IsPermanent.ShouldBeTrue();
        ex.Message.ShouldContain("maximum download size of 1 KB");
    }

    [Fact]
    public async Task Not_found_is_a_permanent_failure()
    {
        await using var server = await TestHttpServer.StartAsync();
        await using var context = new KnowledgeTestContext();

        var ex = await Should.ThrowAsync<IngestionException>(() => Fetcher(context).FetchAsync(server.Url("missing"), Ct));

        ex.IsPermanent.ShouldBeTrue();
        ex.Message.ShouldContain("HTTP 404");
    }

    [Theory]
    [InlineData(500, false)]
    [InlineData(503, false)]
    [InlineData(429, false)]
    [InlineData(408, false)]
    [InlineData(403, true)]
    [InlineData(410, true)]
    public async Task Server_errors_are_transient_and_client_errors_permanent(int status, bool permanent)
    {
        await using var server = await TestHttpServer.StartAsync();
        server.Add("status", "text/plain", "error"u8.ToArray(), status);
        await using var context = new KnowledgeTestContext();

        var ex = await Should.ThrowAsync<IngestionException>(() => Fetcher(context).FetchAsync(server.Url("status"), Ct));

        ex.IsPermanent.ShouldBe(permanent);
        ex.Message.ShouldContain($"HTTP {status}");
    }

    [Fact]
    public async Task Slow_servers_time_out()
    {
        var requestReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var server = await ScriptedHttpServer.StartAsync();
        server.Map("slow", async http =>
        {
            requestReceived.TrySetResult();
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), http.RequestAborted);
            }
            catch (OperationCanceledException)
            {
                // The client gave up.
            }
        });
        await using var context = new KnowledgeTestContext(o => o.FetchTimeoutSeconds = 5);

        var fetch = Fetcher(context).FetchAsync(server.Url("slow"), Ct);
        await requestReceived.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        context.Time.Advance(TimeSpan.FromSeconds(5));

        var ex = await Should.ThrowAsync<IngestionException>(() => fetch.WaitAsync(TimeSpan.FromSeconds(10), Ct));
        ex.IsPermanent.ShouldBeFalse();
        ex.Message.ShouldBe("The request timed out after 5 seconds.");
    }

    [Fact]
    public async Task Caller_cancellation_is_not_reported_as_a_timeout()
    {
        await using var server = await ScriptedHttpServer.StartAsync();
        server.Map("slow", async http => await Task.Delay(TimeSpan.FromSeconds(30), http.RequestAborted).ContinueWith(_ => { }, TaskScheduler.Default));
        await using var context = new KnowledgeTestContext();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await Should.ThrowAsync<OperationCanceledException>(() => Fetcher(context).FetchAsync(server.Url("slow"), cancellation.Token));
    }

    [Fact]
    public async Task Configured_user_agent_is_sent()
    {
        await using var server = await ScriptedHttpServer.StartAsync();
        server.MapText("ua", "ok");
        await using var context = new KnowledgeTestContext(o => o.UserAgent = "KnowledgeAgentTests/2.0 (+https://example.test)");

        await Fetcher(context).FetchAsync(server.Url("ua"), Ct);

        server.Requests.ShouldContain(r => r.Path == "/ua" && r.UserAgent == "KnowledgeAgentTests/2.0 (+https://example.test)");
    }

    [Fact]
    public async Task Content_disposition_file_name_is_reported()
    {
        await using var server = await ScriptedHttpServer.StartAsync();
        server.Map("download", async http =>
        {
            http.Response.ContentType = "application/octet-stream";
            http.Response.Headers.ContentDisposition = "attachment; filename=\"notes.md\"";
            await http.Response.WriteAsync("# Notes", http.RequestAborted);
        });
        await using var context = new KnowledgeTestContext();

        var result = await Fetcher(context).FetchAsync(server.Url("download"), Ct);

        result.FileName.ShouldBe("notes.md");
        result.MediaType.ShouldBe("application/octet-stream");
    }

    private static UrlFetcher Fetcher(KnowledgeTestContext context) => context.Services.GetRequiredService<UrlFetcher>();
}
