using System.Text.Json;
using KnowledgeAgent.Features.Chat;
using KnowledgeAgent.Features.Widgets;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace KnowledgeAgent.Tests.Features.Widgets;

public sealed class WidgetRulesTests
{
    [Theory]
    [InlineData("https://www.Example.com/", "https://www.example.com")]
    [InlineData(" http://localhost:3000 ", "http://localhost:3000")]
    [InlineData("https://example.com:443", "https://example.com")]
    [InlineData("https://*.example.com", "https://*.example.com")]
    [InlineData("https://bücher.de", "https://xn--bcher-kva.de")]
    public void Allowed_origins_are_normalized(string origin, string expected) =>
        WidgetOrigins.Normalize(origin).ShouldBe(expected);

    [Theory]
    [InlineData("")]
    [InlineData("example.com")]
    [InlineData("https://example.com/page")]
    [InlineData("https://example.com?q=1")]
    [InlineData("ftp://example.com")]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://user:pass@example.com")]
    public void Anything_but_an_origin_is_refused(string origin) => WidgetOrigins.Normalize(origin).ShouldBeNull();

    [Fact]
    public void Only_png_jpeg_and_webp_content_counts_as_an_image()
    {
        ImageTypes.Sniff([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0]).ShouldBe("image/png");
        ImageTypes.Sniff([0xFF, 0xD8, 0xFF, 0xE0]).ShouldBe("image/jpeg");
        ImageTypes.Sniff("RIFF\0\0\0\0WEBPVP8 "u8).ShouldBe("image/webp");
        ImageTypes.Sniff("<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>"u8).ShouldBeNull();
        ImageTypes.Sniff("GIF89a"u8).ShouldBeNull();
        ImageTypes.Sniff([]).ShouldBeNull();
    }

    [Fact]
    public void The_frame_may_be_embedded_only_by_the_allowed_sites()
    {
        WidgetFrameEndpoints.ContentSecurityPolicy([]).ShouldContain("frame-ancestors *");
        var policy = WidgetFrameEndpoints.ContentSecurityPolicy(["https://www.example.com", "https://*.shop.example"]);
        policy.ShouldEndWith("frame-ancestors https://www.example.com https://*.shop.example");
        policy.ShouldContain("script-src 'self'");
        policy.ShouldContain("connect-src 'self'");
    }

    [Fact]
    public void The_preview_may_be_embedded_only_by_the_portal()
    {
        var policy = WidgetFrameEndpoints.PreviewContentSecurityPolicy;
        policy.ShouldEndWith("frame-ancestors 'self'");
        policy.ShouldContain("script-src 'self'");
        policy.ShouldContain("connect-src 'self'");
    }

    [Fact]
    public void Opening_hours_follow_the_widget_time_zone()
    {
        var hours = new WidgetAvailability
        {
            Always = false,
            TimeZone = "Europe/Prague",
            Hours = [new WidgetOpeningHours(DayOfWeek.Monday, new TimeOnly(9, 0), new TimeOnly(17, 0))],
        };

        // Monday 2026-11-02: 08:30 UTC is 09:30 in Prague (CET, UTC+1).
        WidgetSchedule.IsOpen(hours, new DateTimeOffset(2026, 11, 2, 8, 30, 0, TimeSpan.Zero)).ShouldBeTrue();
        WidgetSchedule.IsOpen(hours, new DateTimeOffset(2026, 11, 2, 7, 30, 0, TimeSpan.Zero)).ShouldBeFalse();
        WidgetSchedule.IsOpen(hours, new DateTimeOffset(2026, 11, 2, 16, 0, 0, TimeSpan.Zero)).ShouldBeFalse("17:00 in Prague is closing time");
        WidgetSchedule.IsOpen(hours, new DateTimeOffset(2026, 11, 3, 10, 0, 0, TimeSpan.Zero)).ShouldBeFalse("closed on Tuesdays");
        WidgetSchedule.IsOpen(new WidgetAvailability(), new DateTimeOffset(2026, 11, 3, 3, 0, 0, TimeSpan.Zero)).ShouldBeTrue();
    }

    [Fact]
    public void Visitors_see_the_answer_and_its_sources_but_nothing_internal()
    {
        var citation = new CitationDto("S1", Guid.NewGuid(), Guid.NewGuid(), "Returns policy", "https://shop.example/returns", null, "Thirty days.", 0.9);
        var fileCitation = citation with { Ref = "S2", SourceTitle = "Handbook.pdf", SourceUrl = null };
        var result = new ToolResultEvent("c1", "search_knowledge_base", false, "2 passages from 1 source", 12, [citation, fileCitation]);

        VisitorChatEvents.ToVisitor(new AgentStartedEvent("anthropic", "claude-opus-5-5"), WidgetChoices.CitationsLinks).ShouldBeNull();
        VisitorChatEvents.ToVisitor(new UsageEvent("anthropic", "claude-opus-5-5", 10, 5, 15), WidgetChoices.CitationsLinks).ShouldBeNull();
        VisitorChatEvents.ToVisitor(new ReasoningDeltaEvent("thinking"), WidgetChoices.CitationsLinks).ShouldBeNull();
        VisitorChatEvents.ToVisitor(new TextDeltaEvent("research notes") { Agent = "researcher", ParentCallId = "c9" }, WidgetChoices.CitationsLinks).ShouldBeNull();
        VisitorChatEvents.ToVisitor(new TextDeltaEvent("Hello"), WidgetChoices.CitationsLinks).ShouldBe(new TextDeltaEvent("Hello"));

        var call = VisitorChatEvents.ToVisitor(new ToolCallEvent("c1", "search_knowledge_base", JsonDocument.Parse("""{"query":"returns"}""").RootElement), WidgetChoices.CitationsLinks)
            .ShouldBeOfType<ToolCallEvent>();
        call.Arguments.ShouldBeNull();

        var linked = VisitorChatEvents.ToVisitor(result, WidgetChoices.CitationsLinks).ShouldBeOfType<ToolResultEvent>();
        linked.Summary.ShouldBeEmpty();
        linked.Citations.Select(c => (c.Ref, c.SourceUrl, c.SourceId)).ShouldBe([("S1", "https://shop.example/returns", Guid.Empty), ("S2", null, Guid.Empty)]);
        VisitorChatEvents.ToVisitor(result, WidgetChoices.CitationsTitles).ShouldBeOfType<ToolResultEvent>().Citations.ShouldAllBe(c => c.SourceUrl == null);
        VisitorChatEvents.ToVisitor(result, WidgetChoices.CitationsHidden).ShouldBeOfType<ToolResultEvent>().Citations.ShouldBeEmpty();

        var failed = VisitorChatEvents.ToVisitor(new ErrorEvent("Npgsql timeout at db:5432", ChatErrorCodes.InternalError), WidgetChoices.CitationsLinks).ShouldBeOfType<ErrorEvent>();
        failed.Message.ShouldBe(VisitorErrorTexts.Failed);
        VisitorChatEvents.ToVisitor(new ErrorEvent("No chat model", ChatErrorCodes.ModelNotConfigured), WidgetChoices.CitationsLinks)
            .ShouldBeOfType<ErrorEvent>().Message.ShouldBe(VisitorErrorTexts.Unavailable);
    }

    [Fact]
    public void Visitor_tokens_name_one_visitor_of_one_widget_and_cannot_be_forged()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var tokens = new VisitorTokens(new EphemeralDataProtectionProvider(), Options.Create(new WidgetOptions { VisitorTokenLifetimeDays = 30 }), time);
        var identity = new VisitorIdentity(Guid.NewGuid(), Guid.NewGuid());

        var (token, expiresAt) = tokens.Issue(identity);

        tokens.Read(token).ShouldBe(identity);
        expiresAt.ShouldBe(time.GetUtcNow().AddDays(30));
        tokens.Read(token[..^2] + (token[^2] == 'A' ? "B" : "A") + token[^1]).ShouldBeNull();
        tokens.Read("not-a-token").ShouldBeNull();
        tokens.Read(null).ShouldBeNull();
        new VisitorTokens(new EphemeralDataProtectionProvider(), Options.Create(new WidgetOptions()), time).Read(token)
            .ShouldBeNull("another key ring cannot read it");
    }

    [Fact]
    public void Visitor_tokens_carry_whether_the_host_website_verified_the_user()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var tokens = new VisitorTokens(new EphemeralDataProtectionProvider(), Options.Create(new WidgetOptions()), time);
        var verified = new VisitorIdentity(Guid.NewGuid(), Guid.NewGuid(), Verified: true);

        tokens.Read(tokens.Issue(verified).Token).ShouldBe(verified);
        tokens.Read(tokens.Issue(verified with { Verified = false }).Token)!.Verified.ShouldBeFalse();
    }

    [Fact]
    public void Preview_tokens_are_marked_and_expire_within_the_hour()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var tokens = new VisitorTokens(new EphemeralDataProtectionProvider(), Options.Create(new WidgetOptions()), time);
        var preview = new VisitorIdentity(Guid.NewGuid(), Guid.NewGuid(), Preview: true);
        var visitor = preview with { Preview = false };

        var (token, expiresAt) = tokens.Issue(preview);
        var visitorToken = tokens.Issue(visitor).Token;

        expiresAt.ShouldBe(time.GetUtcNow().AddMinutes(60));
        tokens.Read(token).ShouldBe(preview);
        tokens.Read(tokens.Issue(preview with { Verified = true }).Token).ShouldBe(preview with { Verified = true });
        tokens.Read(visitorToken)!.Preview.ShouldBeFalse();

        time.Advance(TimeSpan.FromMinutes(61));
        tokens.Read(token).ShouldBeNull("a copied preview token soon stops working");
        tokens.Read(visitorToken).ShouldBe(visitor);
    }

    [Fact]
    public void Identity_verification_checks_the_hash_in_constant_time_and_keeps_attributes_small()
    {
        var protection = new EphemeralDataProtectionProvider();
        var identity = new WidgetIdentity(protection, Microsoft.Extensions.Logging.Abstractions.NullLogger<WidgetIdentity>.Instance);
        var secret = WidgetIdentity.NewSecret();
        var widget = new Widget { ProtectedIdentitySecret = identity.Protect(secret) };
        var hash = WidgetIdentity.Hash(secret, "42");
        hash.ShouldBe(Convert.ToHexStringLower(System.Security.Cryptography.HMACSHA256.HashData(System.Text.Encoding.UTF8.GetBytes(secret), "42"u8.ToArray())));
        var attributes = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
            """{"plan":"pro","seats":3,"beta":false,"nested":{"a":1},"bad key":"x","empty":" "}""")!;

        var user = identity.Verify(widget, new VisitorUserRequest(" 42 ", hash, "Ada", "not an email", attributes)).ShouldNotBeNull();

        user.UserId.ShouldBe("42");
        user.Name.ShouldBe("Ada");
        user.Email.ShouldBeNull("an invalid email is left out");
        user.Attributes.Keys.ShouldBe(["plan", "seats", "beta", "nested", "bad key", "empty"], ignoreOrder: true, "values pass as sent; the contact's merge checks them");
        identity.Verify(widget, new VisitorUserRequest("43", hash)).ShouldBeNull();
        identity.Verify(widget, new VisitorUserRequest("42", hash[..^1])).ShouldBeNull();
        identity.Verify(widget, new VisitorUserRequest(null, hash)).ShouldBeNull();
        identity.Verify(new Widget(), new VisitorUserRequest("42", hash)).ShouldBeNull("a widget without a secret verifies nobody");
        identity.Verify(widget, null).ShouldBeNull();
    }

    [Fact]
    public void Rate_limits_count_requests_in_a_sliding_minute()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var limiter = new WidgetRateLimiter(time);

        limiter.TryAcquire("visitor:a", 2, out _).ShouldBeTrue();
        time.Advance(TimeSpan.FromSeconds(20));
        limiter.TryAcquire("visitor:a", 2, out _).ShouldBeTrue();
        limiter.TryAcquire("visitor:a", 2, out var retryAfter).ShouldBeFalse();
        retryAfter.ShouldBe(TimeSpan.FromSeconds(40));
        limiter.TryAcquire("visitor:b", 2, out _).ShouldBeTrue("each key has its own window");

        time.Advance(TimeSpan.FromSeconds(40));
        limiter.TryAcquire("visitor:a", 2, out _).ShouldBeTrue();
        WidgetRateLimiter.RetryAfterSeconds(TimeSpan.FromMilliseconds(100)).ShouldBe("1");
    }

    [Fact]
    public async Task The_rate_limiter_grants_no_more_than_the_limit_while_a_cleanup_removes_the_key_s_old_window()
    {
        const int limit = 5;
        for (var round = 0; round < 20; round++)
        {
            var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
            var limiter = new WidgetRateLimiter(time);

            // Enough stale windows that every request below cleans up, including the old window of the key it asks for.
            for (var i = 0; i <= 10_000; i++)
            {
                limiter.TryAcquire($"old:{i}", limit, out _);
            }

            limiter.TryAcquire("visitor:a", limit, out _);
            time.Advance(TimeSpan.FromMinutes(2));

            var granted = await Task.WhenAll(Enumerable.Range(0, 40).Select(request => Task.Run(() => limiter.TryAcquire("visitor:a", limit, out _))));

            granted.Count(ok => ok).ShouldBe(limit, $"round {round}");
        }
    }
}
