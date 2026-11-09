using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using KnowledgeAgent.Api.Tests.Support;
using KnowledgeAgent.Features.Agents;
using KnowledgeAgent.Features.Chat;
using KnowledgeAgent.Features.Conversations;
using KnowledgeAgent.Features.Feedback;
using KnowledgeAgent.Features.Chat.Tools;
using KnowledgeAgent.Features.Sources;
using KnowledgeAgent.Features.Sources.Folders;
using KnowledgeAgent.Features.Widgets;
using KnowledgeAgent.TestKit;

namespace KnowledgeAgent.Api.Tests.Scenarios;

public abstract class WidgetScenarios(AppFixture fixture)
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task Widgets_are_validated_and_keep_their_agent_from_being_deleted()
    {
        var app = await fixture.GetAppAsync();
        var agent = await CreateAgentAsync(app, folderId: null);
        var builtin = (await app.Client.GetJsonAsync<AgentDto[]>("/api/agents")).Single(item => item.IsBuiltIn);

        var invalid = new SaveWidgetRequest(
            "Shop help",
            builtin.Id,
            Settings: new WidgetSettings
            {
                Appearance = new WidgetAppearance
                {
                    BrandColor = "teal",
                    Theme = "neon",
                    Preset = "aurora",
                    Colors = new WidgetColors { Light = new() { ["accent"] = "blue" }, Dark = new() { ["shadow"] = "#000000" } },
                },
                Behavior = new WidgetBehavior { SuggestedQuestions = ["a", "b", "c", "d", "e"], Texts = new() { ["unknown"] = "x" } },
                Security = new WidgetSecurity { AllowedOrigins = ["https://shop.example/page"], MessagesPerMinute = 0 },
                Handover = new WidgetHandover { WaitMinutes = 0 },
                Conversations = new WidgetConversations { AiIdleMinutes = 4, HumanIdleHours = 169 },
            });
        using (var refused = await app.Client.PostJsonAsync("/api/widgets", invalid))
        {
            (await refused.ReadProblemAsync(HttpStatusCode.BadRequest)).FieldErrors.Keys.ShouldBe(
                [
                    "agentId", "settings.appearance.theme", "settings.appearance.preset", "settings.appearance.colors.light",
                    "settings.appearance.colors.dark", "settings.appearance.brandColor", "settings.behavior.suggestedQuestions",
                    "settings.behavior.texts", "settings.security.allowedOrigins", "settings.security.messagesPerMinute",
                    "settings.handover.waitMinutes", "settings.conversations.aiIdleMinutes", "settings.conversations.humanIdleHours",
                ],
                ignoreOrder: true);
        }

        var widget = await CreateWidgetAsync(app, agent.Id, new WidgetSettings
        {
            Security = new WidgetSecurity { AllowedOrigins = ["https://Shop.Example/", "https://*.shop.example"] },
        });
        widget.PublicKey.ShouldStartWith("wk_");
        widget.PublicKey.Length.ShouldBe(27);
        widget.Settings.Security.AllowedOrigins.ShouldBe(["https://shop.example", "https://*.shop.example"]);
        widget.Settings.Appearance.Preset.ShouldBe(WidgetChoices.PresetGlow);
        widget.Settings.Appearance.Colors.Light.ShouldBeEmpty();
        widget.Settings.Appearance.ShowFooter.ShouldBeTrue();
        widget.Settings.Handover.WaitMinutes.ShouldBe(15);
        widget.Settings.Conversations.ShouldBe(new WidgetConversations { CloseIdle = true, AiIdleMinutes = 30, HumanIdleHours = 24 });
        (await app.Client.GetJsonAsync<WidgetDto[]>("/api/widgets")).ShouldContain(item => item.Id == widget.Id);
        var options = await app.Client.GetJsonAsync<WidgetOptionsDto>("/api/widgets/options");
        options.LauncherIcons.ShouldContain("message-circle");
        options.Presets.ShouldBe(["glow", "glass", "solid"]);
        options.ColorTokens.ShouldContain("userBubble");
        options.TextKeys.ShouldContain("keepChatting");
        options.Defaults.Conversations.ShouldBe(new WidgetConversations());

        using (var inUse = await app.Client.DeleteAsync($"/api/agents/{agent.Id}", Ct))
        {
            (await inUse.ReadProblemAsync(HttpStatusCode.BadRequest)).FieldErrors["id"].ShouldHaveSingleItem().ShouldContain("Shop help");
        }

        var avatar = await UploadAsync(app, widget.Id, Png, "avatar.png");
        avatar.StatusCode.ShouldBe(HttpStatusCode.Created);
        var asset = await avatar.Content.ReadFromJsonAsync<WidgetAssetDto>(TestJson.Options, Ct);
        asset.ShouldNotBeNull().Url.ShouldBe($"/widget/v1/media/{asset.Id}");
        using (var svg = await UploadAsync(app, widget.Id, "<svg xmlns=\"http://www.w3.org/2000/svg\"/>"u8.ToArray(), "logo.png"))
        {
            (await svg.ReadProblemAsync(HttpStatusCode.BadRequest)).FieldErrors.ShouldContainKey("file");
        }

        using (var media = await app.Client.GetAsync(asset.Url, Ct))
        {
            media.StatusCode.ShouldBe(HttpStatusCode.OK);
            media.Content.Headers.ContentType?.MediaType.ShouldBe("image/png");
            media.Headers.GetValues("X-Content-Type-Options").ShouldBe(["nosniff"]);
            (await media.Content.ReadAsByteArrayAsync(Ct)).ShouldBe(Png);
        }

        var withAvatar = widget.Settings with { Appearance = widget.Settings.Appearance with { AvatarAssetId = asset.Id } };
        using (var update = await app.Client.PutJsonAsync($"/api/widgets/{widget.Id}", new SaveWidgetRequest("Shop help desk", agent.Id, Settings: withAvatar)))
        {
            var updated = await update.ReadJsonAsync<WidgetDto>();
            updated.Name.ShouldBe("Shop help desk");
            updated.Settings.Appearance.AvatarAssetId.ShouldBe(asset.Id);
        }

        var foreignAsset = widget.Settings with { Appearance = widget.Settings.Appearance with { AvatarAssetId = Guid.NewGuid() } };
        using (var foreign = await app.Client.PutJsonAsync($"/api/widgets/{widget.Id}", new SaveWidgetRequest("Shop help desk", agent.Id, Settings: foreignAsset)))
        {
            (await foreign.ReadProblemAsync(HttpStatusCode.BadRequest)).FieldErrors.ShouldContainKey("settings.appearance");
        }

        using (var delete = await app.Client.DeleteAsync($"/api/widgets/{widget.Id}", Ct))
        {
            delete.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        using (var freed = await app.Client.DeleteAsync($"/api/agents/{agent.Id}", Ct))
        {
            freed.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task A_visitor_chats_with_the_widget_agent_and_sees_only_what_visitors_may_see()
    {
        var app = await fixture.GetAppAsync();
        app.ChatModels.Reset();
        var marker = ApiClient.UniqueWord();
        var folder = await CreateFolderAsync(app);
        var inside = await AddReadyTextAsync(app, "Returns policy", $"The {marker} return window is thirty days.", folder.Id);
        await AddReadyTextAsync(app, "Internal margins", $"The {marker} margin is forty percent.", folderId: null);
        var agent = await CreateAgentAsync(app, folder.Id);
        var widget = await CreateWidgetAsync(app, agent.Id, new WidgetSettings
        {
            Appearance = new WidgetAppearance
            {
                Title = "Shop help",
                BrandColor = "#7c3aed",
                Preset = WidgetChoices.PresetSolid,
                Colors = new WidgetColors { Light = new() { ["header"] = "#1E3A8A", ["text"] = " " }, Dark = new() { ["glow"] = "#f472b6" } },
                ShowFooter = false,
            },
            Behavior = new WidgetBehavior { CitationMode = WidgetChoices.CitationsLinks, SuggestedQuestions = ["Can I return items?"] },
        });
        var publicBase = $"/api/public/widgets/{widget.PublicKey}";

        using (var configRequest = new HttpRequestMessage(HttpMethod.Get, $"{publicBase}/config"))
        {
            configRequest.Headers.Add("Origin", "https://any-shop.example");
            using var configResponse = await app.Client.SendAsync(configRequest, Ct);
            var config = await configResponse.ReadJsonAsync<WidgetPublicConfigDto>();
            config.Title.ShouldBe("Shop help");
            config.BrandColor.ShouldBe("#7c3aed");
            config.Preset.ShouldBe("solid");
            config.Colors.Light.ShouldBe(new Dictionary<string, string> { ["header"] = "#1e3a8a" });
            config.Colors.Dark.ShouldBe(new Dictionary<string, string> { ["glow"] = "#f472b6" });
            config.ShowFooter.ShouldBeFalse();
            config.SuggestedQuestions.ShouldBe(["Can I return items?"]);
            configResponse.Headers.GetValues("Access-Control-Allow-Origin").ShouldBe(["*"]);
        }

        using (var anonymous = await app.Client.PostJsonAsync($"{publicBase}/messages", new VisitorMessageRequest("Hello")))
        {
            await anonymous.ReadProblemAsync(HttpStatusCode.Unauthorized);
        }

        var session = await StartSessionAsync(app, widget.PublicKey);
        session.ConversationId.ShouldBeNull();

        var stream = await SendAsync(app, widget.PublicKey, session.Token, new VisitorMessageRequest($"How long is the {marker} return window?"));
        stream.Errors.ShouldBeEmpty(stream.Describe());
        stream.Types.Distinct().ShouldBeSubsetOf([ChatEventTypes.Conversation, ChatEventTypes.ToolCall, ChatEventTypes.ToolResult, ChatEventTypes.TextDelta, ChatEventTypes.StepFinished, ChatEventTypes.Done]);
        stream.All<ToolCallEvent>(ChatEventTypes.ToolCall).ShouldAllBe(call => call.Arguments == null);
        var results = stream.All<ToolResultEvent>(ChatEventTypes.ToolResult);
        results.ShouldNotBeEmpty(stream.Describe());
        results.ShouldAllBe(result => result.Summary == string.Empty);
        var citations = results.SelectMany(result => result.Citations).ToList();
        citations.ShouldNotBeEmpty();
        citations.ShouldAllBe(citation => citation.SourceTitle == "Returns policy" && citation.SourceId == Guid.Empty);
        var conversationId = stream.ConversationId;

        var history = await GetAsVisitorAsync<VisitorConversationDto>(app, $"{publicBase}/conversations/{conversationId}", session.Token);
        history.Status.ShouldBe("ai");
        history.Messages.Select(message => message.Author).ShouldBe(["visitor", "assistant"]);
        history.Messages[1].Text.ShouldBe(stream.Text);
        history.Messages[1].Citations.ShouldNotBeEmpty();
        history.Messages[1].Citations.ShouldAllBe(citation => citation.Title == "Returns policy");

        var resumed = await StartSessionAsync(app, widget.PublicKey, session.Token);
        resumed.VisitorId.ShouldBe(session.VisitorId);
        resumed.ConversationId.ShouldBe(conversationId);

        var stranger = await StartSessionAsync(app, widget.PublicKey);
        stranger.VisitorId.ShouldNotBe(session.VisitorId);
        using (var peek = new HttpRequestMessage(HttpMethod.Get, $"{publicBase}/conversations/{conversationId}"))
        {
            peek.Headers.Authorization = new AuthenticationHeaderValue("Bearer", stranger.Token);
            using var response = await app.Client.SendAsync(peek, Ct);
            await response.ReadProblemAsync(HttpStatusCode.NotFound);
        }

        using (var hijack = await PostAsVisitorAsync(app, widget.PublicKey, stranger.Token, new VisitorMessageRequest("Me too", conversationId)))
        {
            await hijack.ReadProblemAsync(HttpStatusCode.NotFound);
        }

        var inbox = (await app.Client.GetJsonAsync<ConversationListPageDto>($"/api/conversations?channel=widget&widgetId={widget.Id}")).Items;
        var summary = inbox.ShouldHaveSingleItem();
        summary.Id.ShouldBe(conversationId);
        summary.Channel.ShouldBe("widget");
        summary.Preview.ShouldNotBeNullOrWhiteSpace();
        (await app.Client.GetJsonAsync<ConversationListPageDto>("/api/conversations?channel=portal")).Items.ShouldNotContain(item => item.Id == conversationId);
        using (var badFilter = await app.Client.GetAsync("/api/conversations?status=asleep", Ct))
        {
            (await badFilter.ReadProblemAsync(HttpStatusCode.BadRequest)).FieldErrors.ShouldContainKey("status");
        }

        var staffView = await app.Client.GetJsonAsync<ConversationDto>($"/api/conversations/{conversationId}");
        staffView.WidgetId.ShouldBe(widget.Id);
        staffView.VisitorId.ShouldBe(session.VisitorId);
        staffView.AgentId.ShouldBe(agent.Id);
        staffView.Turns.ShouldHaveSingleItem().ToolCalls.ShouldNotBeEmpty("staff see the whole turn");

        var live = await ReadVisitorEventsAsync(app, $"{publicBase}/conversations/{conversationId}/events?after=0", session.Token, count: 2);
        live.Select(e => (e.Event, e.Id)).ShouldBe([("message", "1"), ("message", "2")]);
        live[0].Deserialize<VisitorMessageDto>().Author.ShouldBe("visitor");
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task A_preview_runs_the_real_widget_as_a_test_that_counts_nowhere_and_asks_nobody()
    {
        await using var app = await fixture.CreateAppAsync();
        app.ChatModels.Reset();
        var agent = await CreateAgentAsync(app, folderId: null);
        var widget = await CreateWidgetAsync(app, agent.Id, new WidgetSettings
        {
            Appearance = new WidgetAppearance { Title = "Demo shop" },
            Behavior = new WidgetBehavior { HandoverEnabled = true },
            Handover = new WidgetHandover { Channel = WidgetChoices.HandoverPortal },
            Security = new WidgetSecurity { DailyMessageCap = 1, AllowedOrigins = ["https://shop.example"] },
        });
        var publicBase = $"/api/public/widgets/{widget.PublicKey}";

        using (var disable = await app.Client.PutJsonAsync($"/api/widgets/{widget.Id}", new SaveWidgetRequest(widget.Name, agent.Id, Enabled: false, Settings: widget.Settings)))
        {
            disable.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        PreviewSession preview;
        using (var started = await app.Client.PostAsync($"/api/widgets/{widget.Id}/preview-session", null, Ct))
        {
            started.Headers.CacheControl?.NoStore.ShouldBeTrue();
            var dto = await started.ReadJsonAsync<WidgetPreviewSessionDto>();
            dto.Config.Title.ShouldBe("Demo shop");
            dto.Config.HandoverEnabled.ShouldBeTrue();
            dto.Session.ConversationId.ShouldBeNull();
            dto.Session.ExpiresAt.ShouldBeLessThanOrEqualTo(DateTimeOffset.UtcNow.AddMinutes(61), "a preview token is short-lived");
            preview = new PreviewSession(dto.Session.Token, dto.Session.VisitorId);
        }

        using (var missing = await app.Client.PostAsync($"/api/widgets/{Guid.NewGuid()}/preview-session", null, Ct))
        {
            await missing.ReadProblemAsync(HttpStatusCode.NotFound);
        }

        // The daily cap is 1, and the preview does not use it up.
        var first = await SendAsync(app, widget.PublicKey, preview.Token, new VisitorMessageRequest("Hello?"));
        first.Errors.ShouldBeEmpty(first.Describe());
        var conversationId = first.ConversationId;
        (await SendAsync(app, widget.PublicKey, preview.Token, new VisitorMessageRequest("And again?", conversationId))).Errors.ShouldBeEmpty();

        var staffView = await app.Client.GetJsonAsync<ConversationDto>($"/api/conversations/{conversationId}");
        (staffView.Channel, staffView.WidgetId, staffView.VisitorId, staffView.AgentId).ShouldBe(("portal", (Guid?)widget.Id, (Guid?)preview.VisitorId, (Guid?)agent.Id));
        staffView.ContactId.ShouldBeNull("a preview makes no contact");
        (await app.Client.GetJsonAsync<ConversationListPageDto>($"/api/conversations?channel=widget&widgetId={widget.Id}")).Items.ShouldBeEmpty("the Inbox never shows a preview");
        (await app.Client.GetJsonAsync<ConversationCountsDto>($"/api/conversations/counts?widgetId={widget.Id}")).All.ShouldBe(0);

        using (var contact = await PostJsonAsVisitorAsync(app, $"{publicBase}/contact", preview.Token, new VisitorContactRequest("Ana", "ana@example.com")))
        {
            (await contact.ReadJsonAsync<VisitorContactDto>()).Email.ShouldBe("ana@example.com");
        }

        using (var rated = await PostJsonAsVisitorAsync(app, $"{publicBase}/conversations/{conversationId}/feedback", preview.Token, new SubmitFeedbackRequest(5)))
        {
            rated.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        (await app.Client.GetJsonAsync<ConversationDto>($"/api/conversations/{conversationId}")).ContactId.ShouldBeNull();

        using (var asked = await PostJsonAsVisitorAsync(app, $"{publicBase}/conversations/{conversationId}/handover", preview.Token, new VisitorHandoverRequest()))
        {
            var outcome = await asked.ReadJsonAsync<VisitorHandoverDto>();
            (outcome.Status, outcome.Started).ShouldBe(("handover_pending", true));
        }

        var waiting = await GetAsVisitorAsync<VisitorConversationDto>(app, $"{publicBase}/conversations/{conversationId}", preview.Token);
        waiting.Status.ShouldBe("handover_pending");
        waiting.Messages[^2].Text.ShouldBe("I've asked a colleague to join. They'll reply here as soon as they can.");
        waiting.Messages[^1].Text.ShouldBe("Preview: nobody is asked. In a live chat, this would ask your team to join (Inbox).");
        using (var none = await app.Client.GetAsync($"/api/conversations/{conversationId}/handover", Ct))
        {
            none.StatusCode.ShouldBe(HttpStatusCode.NoContent, "no request for a person was stored");
        }

        using (var relayed = await PostAsVisitorAsync(app, widget.PublicKey, preview.Token, new VisitorMessageRequest("Is anyone there?", conversationId)))
        {
            relayed.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        }

        using (var cancel = await PostJsonAsVisitorAsync(app, $"{publicBase}/conversations/{conversationId}/handover/cancel", preview.Token, new { }))
        {
            cancel.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        (await GetAsVisitorAsync<VisitorConversationDto>(app, $"{publicBase}/conversations/{conversationId}", preview.Token)).Status.ShouldBe("ai");

        using (var refused = await app.Client.PostJsonAsync($"{publicBase}/sessions", new VisitorSessionRequest(preview.Token)))
        {
            await refused.ReadProblemAsync(HttpStatusCode.NotFound);
        }

        using (var enable = await app.Client.PutJsonAsync($"/api/widgets/{widget.Id}", new SaveWidgetRequest(widget.Name, agent.Id, Enabled: true, Settings: widget.Settings)))
        {
            enable.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        var visitor = await StartSessionAsync(app, widget.PublicKey, preview.Token);
        visitor.VisitorId.ShouldNotBe(preview.VisitorId);
        visitor.ConversationId.ShouldBeNull();

        using (var close = await PostJsonAsVisitorAsync(app, $"{publicBase}/conversations/{conversationId}/close", preview.Token, new { }))
        {
            close.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        (await app.Client.GetJsonAsync<ConversationDto>($"/api/conversations/{conversationId}")).Status.ShouldBe("closed");
    }

    private sealed record PreviewSession(string Token, Guid VisitorId);

    private static async Task<HttpResponseMessage> PostJsonAsVisitorAsync(KnowledgeAgentApp app, string url, string token, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body, body.GetType(), options: TestJson.Options) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await app.Client.SendAsync(request, Ct);
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task Visitors_are_rate_limited_and_each_widget_has_a_daily_cap()
    {
        await using var app = await fixture.CreateAppAsync();
        app.ChatModels.Reset();
        var agent = await CreateAgentAsync(app, folderId: null);
        var widget = await CreateWidgetAsync(app, agent.Id, new WidgetSettings
        {
            Security = new WidgetSecurity { MessagesPerMinute = 1, DailyMessageCap = 2, MaxMessageLength = 100 },
        });

        var first = await StartSessionAsync(app, widget.PublicKey);
        (await SendAsync(app, widget.PublicKey, first.Token, new VisitorMessageRequest("Hello?"))).Errors.ShouldBeEmpty();
        using (var tooFast = await PostAsVisitorAsync(app, widget.PublicKey, first.Token, new VisitorMessageRequest("Again?")))
        {
            tooFast.Headers.RetryAfter.ShouldNotBeNull();
            (await tooFast.ReadProblemAsync(HttpStatusCode.TooManyRequests)).Code.ShouldBe(PublicWidgetEndpoints.RateLimitedCode);
        }

        var second = await StartSessionAsync(app, widget.PublicKey);
        using (var tooLong = await PostAsVisitorAsync(app, widget.PublicKey, second.Token, new VisitorMessageRequest(new string('a', 101))))
        {
            (await tooLong.ReadProblemAsync(HttpStatusCode.BadRequest)).FieldErrors.ShouldContainKey("message");
        }

        using (var huge = await PostAsVisitorAsync(app, widget.PublicKey, second.Token, new VisitorMessageRequest(new string('a', 200_000))))
        {
            huge.IsSuccessStatusCode.ShouldBeFalse("bodies over 64 KB are refused before they are read");
        }

        (await SendAsync(app, widget.PublicKey, second.Token, new VisitorMessageRequest("Hi there"))).Errors.ShouldBeEmpty();

        var third = await StartSessionAsync(app, widget.PublicKey);
        using var capped = await PostAsVisitorAsync(app, widget.PublicKey, third.Token, new VisitorMessageRequest("Anyone?"));
        (await capped.ReadProblemAsync(HttpStatusCode.TooManyRequests)).Code.ShouldBe(PublicWidgetEndpoints.DailyCapCode);
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task The_frame_names_the_sites_allowed_to_embed_it_the_preview_only_the_portal_and_only_built_files_are_served()
    {
        var build = Directory.CreateTempSubdirectory("ka-widget-");
        try
        {
            await File.WriteAllTextAsync(Path.Combine(build.FullName, "frame.html"), "<!doctype html><title>Chat</title><div id=\"root\"></div>", Ct);
            await File.WriteAllTextAsync(Path.Combine(build.FullName, "loader.js"), "console.log('loader');", Ct);
            Directory.CreateDirectory(Path.Combine(build.FullName, "assets"));
            await File.WriteAllTextAsync(Path.Combine(build.FullName, "assets", "frame-abc123.js"), "console.log('frame');", Ct);
            await using var app = await fixture.CreateAppAsync(options => options.Settings["Widgets:AssetsPath"] = build.FullName);
            var agent = await CreateAgentAsync(app, folderId: null);
            var widget = await CreateWidgetAsync(app, agent.Id, new WidgetSettings { Security = new WidgetSecurity { AllowedOrigins = ["https://shop.example"] } });

            using (var frame = await app.Client.GetAsync($"/widget/v1/frame/{widget.PublicKey}", Ct))
            {
                frame.StatusCode.ShouldBe(HttpStatusCode.OK);
                frame.Content.Headers.ContentType?.MediaType.ShouldBe("text/html");
                frame.Headers.GetValues("Content-Security-Policy").Single().ShouldEndWith("frame-ancestors https://shop.example");
                (await frame.Content.ReadAsStringAsync(Ct)).ShouldContain("<div id=\"root\">");
            }

            using (var loader = await app.Client.GetAsync("/widget/v1/loader.js", Ct))
            {
                loader.StatusCode.ShouldBe(HttpStatusCode.OK);
                loader.Headers.CacheControl?.MaxAge.ShouldBe(TimeSpan.FromMinutes(5));
            }

            using (var asset = await app.Client.GetAsync("/widget/v1/assets/frame-abc123.js", Ct))
            {
                asset.StatusCode.ShouldBe(HttpStatusCode.OK);
                asset.Headers.CacheControl.ShouldNotBeNull().ToString().ShouldContain("immutable");
            }

            foreach (var path in new[] { "/widget/v1/frame.html", "/widget/v1/assets/missing.js", "/widget/v1/assets/..%2F..%2Fappsettings.json", $"/widget/v1/frame/wk_{new string('x', 24)}" })
            {
                using var missing = await app.Client.GetAsync(path, Ct);
                missing.StatusCode.ShouldBe(HttpStatusCode.NotFound, path);
            }

            using (var disable = await app.Client.PutJsonAsync($"/api/widgets/{widget.Id}", new SaveWidgetRequest(widget.Name, agent.Id, Enabled: false, Settings: widget.Settings)))
            {
                disable.StatusCode.ShouldBe(HttpStatusCode.OK);
            }

            using (var disabled = await app.Client.GetAsync($"/widget/v1/frame/{widget.PublicKey}", Ct))
            {
                disabled.StatusCode.ShouldBe(HttpStatusCode.NotFound);
            }

            using (var preview = await app.Client.GetAsync("/widget/v1/preview", Ct))
            {
                preview.StatusCode.ShouldBe(HttpStatusCode.OK);
                preview.Content.Headers.ContentType?.MediaType.ShouldBe("text/html");
                preview.Headers.GetValues("Content-Security-Policy").Single().ShouldEndWith("frame-ancestors 'self'");
                (await preview.Content.ReadAsStringAsync(Ct)).ShouldContain("<div id=\"root\">");
            }

            using var config = await app.Client.GetAsync($"/api/public/widgets/{widget.PublicKey}/config", Ct);
            await config.ReadProblemAsync(HttpStatusCode.NotFound);
        }
        finally
        {
            build.Delete(recursive: true);
        }
    }

    private static async Task<AgentDto> CreateAgentAsync(KnowledgeAgentApp app, Guid? folderId)
    {
        using var response = await app.Client.PostJsonAsync(
            "/api/agents",
            new SaveAgentRequest("Shop assistant", ApiClient.UniqueWord("shop-"), FolderIds: folderId is { } id ? [id] : [], Tools: [ToolNames.SearchKnowledgeBase]));
        return await response.ReadJsonAsync<AgentDto>(HttpStatusCode.Created);
    }

    private static async Task<WidgetDto> CreateWidgetAsync(KnowledgeAgentApp app, Guid agentId, WidgetSettings settings)
    {
        using var response = await app.Client.PostJsonAsync("/api/widgets", new SaveWidgetRequest("Shop help", agentId, Settings: settings));
        var widget = await response.ReadJsonAsync<WidgetDto>(HttpStatusCode.Created);
        response.Headers.Location?.ToString().ShouldBe($"/api/widgets/{widget.Id}");
        return widget;
    }

    private static async Task<HttpResponseMessage> UploadAsync(KnowledgeAgentApp app, Guid widgetId, byte[] content, string fileName)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "file", fileName);
        return await app.Client.PostAsync($"/api/widgets/{widgetId}/assets", form, Ct);
    }

    private static async Task<VisitorSessionDto> StartSessionAsync(KnowledgeAgentApp app, string key, string? token = null)
    {
        using var response = await app.Client.PostJsonAsync($"/api/public/widgets/{key}/sessions", new VisitorSessionRequest(token));
        return await response.ReadJsonAsync<VisitorSessionDto>();
    }

    private static Task<HttpResponseMessage> PostAsVisitorAsync(KnowledgeAgentApp app, string key, string token, VisitorMessageRequest body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/public/widgets/{key}/messages")
        {
            Content = JsonContent.Create(body, options: TestJson.Options),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(SseReader.AcceptHeader);
        return app.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, Ct);
    }

    private static async Task<ChatStream> SendAsync(KnowledgeAgentApp app, string key, string token, VisitorMessageRequest body)
    {
        using var response = await PostAsVisitorAsync(app, key, token, body);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new ShouldAssertException($"POST messages returned {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(Ct)}");
        }

        var events = await SseReader.ReadAllAsync(response, Ct);
        return new ChatStream(response.Content.Headers.ContentType?.MediaType, response.Headers.CacheControl?.ToString(), null, events);
    }

    private static async Task<T> GetAsVisitorAsync<T>(KnowledgeAgentApp app, string url, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await app.Client.SendAsync(request, Ct);
        return await response.ReadJsonAsync<T>();
    }

    private static async Task<List<SseEvent>> ReadVisitorEventsAsync(KnowledgeAgentApp app, string url, string token, int count)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(SseReader.AcceptHeader);
        using var response = await app.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        var events = new List<SseEvent>();
        await foreach (var sseEvent in SseReader.ReadAsync(response, timeout.Token))
        {
            events.Add(sseEvent);
            if (events.Count == count)
            {
                break;
            }
        }

        return events;
    }

    private static async Task<SourceFolderDto> CreateFolderAsync(KnowledgeAgentApp app)
    {
        using var response = await app.Client.PostJsonAsync("/api/sources/folders", new CreateSourceFolderRequest(ApiClient.UniqueWord("folder"), null));
        return await response.ReadJsonAsync<SourceFolderDto>(HttpStatusCode.Created);
    }

    private static async Task<SourceDto> AddReadyTextAsync(KnowledgeAgentApp app, string title, string content, Guid? folderId)
    {
        using var response = await app.Client.PostJsonAsync("/api/sources/text", new AddTextSourceRequest(title, content, FolderId: folderId));
        var source = await response.ReadJsonAsync<SourceDto>(HttpStatusCode.Accepted);
        return await app.WaitForSourceAsync(source.Id);
    }
}

public sealed class InMemoryWidgetScenarios(InMemoryAppFixture fixture) : WidgetScenarios(fixture), IClassFixture<InMemoryAppFixture>;

public sealed class ContainersWidgetScenarios(ContainersAppFixture fixture) : WidgetScenarios(fixture), IClassFixture<ContainersAppFixture>;
