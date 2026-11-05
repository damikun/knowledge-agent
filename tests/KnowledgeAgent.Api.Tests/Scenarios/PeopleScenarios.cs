using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KnowledgeAgent.Api.Tests.Support;
using KnowledgeAgent.Features.Agents;
using KnowledgeAgent.Features.Chat;
using KnowledgeAgent.Features.Chat.Tools;
using KnowledgeAgent.Features.Conversations;
using KnowledgeAgent.Features.People;
using KnowledgeAgent.Features.Widgets;
using KnowledgeAgent.TestKit;

namespace KnowledgeAgent.Api.Tests.Scenarios;

public abstract class PeopleScenarios(AppFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task A_verified_user_takes_over_the_anonymous_history_of_the_browser_and_follows_them_to_another_device()
    {
        var app = await fixture.GetAppAsync();
        app.ChatModels.Reset();
        var widget = await CreateWidgetAsync(app);
        var secret = await CreateSecretAsync(app, widget.Id);
        var userId = ApiClient.UniqueWord("user-");

        var anonymous = await StartSessionAsync(app, widget.PublicKey);
        anonymous.Identified.ShouldBeFalse();
        var chat = await SendAsync(app, widget.PublicKey, anonymous.Token, "Where is my parcel?");
        var anonymousPerson = await PersonOfAsync(app, chat);
        anonymousPerson.Identified.ShouldBeFalse();
        anonymousPerson.Name.ShouldStartWith("Visitor ");

        var user = new VisitorUserRequest(userId, Hash(secret, userId), "Ada Lovelace", "ada@example.com", Attributes(new { plan = "pro", seats = 3, beta = true }));
        var signedIn = await StartSessionAsync(app, widget.PublicKey, anonymous.Token, user);

        signedIn.Identified.ShouldBeTrue();
        signedIn.VisitorId.ShouldBe(anonymous.VisitorId, "the browser stays the same visitor");
        signedIn.ConversationId.ShouldBe(chat);
        signedIn.Email.ShouldBe("ada@example.com");
        var person = await PersonOfAsync(app, chat);
        person.ExternalId.ShouldBe(userId);
        person.Name.ShouldBe("Ada Lovelace");
        person.Attributes.ToDictionary(item => item.Key, item => item.Value.GetRawText()).ShouldBe(
            new Dictionary<string, string> { ["plan"] = "\"pro\"", ["seats"] = "3", ["beta"] = "true" },
            ignoreOrder: true);
        person.Attributes.ShouldAllBe(item => item.Source == AttributeSources.Verified);
        person.Attributes.Single(item => item.Key == "seats").Type.ShouldBe(AttributeTypes.Number);
        person.Stats.Conversations.ShouldBe(1);
        using (var gone = await app.Client.GetAsync($"/api/people/{anonymousPerson.Id}", Ct))
        {
            gone.StatusCode.ShouldBe(HttpStatusCode.NotFound, "the anonymous person merged into the user");
        }

        var phone = await StartSessionAsync(app, widget.PublicKey, user: user);
        phone.VisitorId.ShouldNotBe(anonymous.VisitorId);
        phone.ConversationId.ShouldBe(chat);
        (await GetAsVisitorAsync(app, $"/api/public/widgets/{widget.PublicKey}/conversations/{chat}", phone.Token)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await app.Client.GetJsonAsync<PersonDto>($"/api/people/{person.Id}")).Widgets.ShouldHaveSingleItem().Id.ShouldBe(widget.Id);

        var otherId = ApiClient.UniqueWord("user-");
        var other = await StartSessionAsync(app, widget.PublicKey, phone.Token, new VisitorUserRequest(otherId, Hash(secret, otherId)));
        other.VisitorId.ShouldNotBe(phone.VisitorId);
        other.ConversationId.ShouldBeNull();
        (await GetAsVisitorAsync(app, $"/api/public/widgets/{widget.PublicKey}/conversations/{chat}", other.Token)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // A page without the user on a widget that verifies users is a signed-out page: a new anonymous visitor.
        var signedOut = await StartSessionAsync(app, widget.PublicKey, signedIn.Token);
        signedOut.Identified.ShouldBeFalse();
        signedOut.VisitorId.ShouldNotBe(signedIn.VisitorId);
        signedOut.ConversationId.ShouldBeNull();
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task An_invalid_hash_or_a_widget_without_a_secret_leaves_the_visitor_anonymous()
    {
        var app = await fixture.GetAppAsync();
        var widget = await CreateWidgetAsync(app);
        var userId = ApiClient.UniqueWord("user-");

        // No secret yet: identify is ignored, whatever the hash.
        var withoutSecret = await StartSessionAsync(app, widget.PublicKey, user: new VisitorUserRequest(userId, Hash("guess", userId)));
        withoutSecret.Identified.ShouldBeFalse();

        var secret = await CreateSecretAsync(app, widget.Id);
        (await StartSessionAsync(app, widget.PublicKey, user: new VisitorUserRequest(userId, Hash("wrong-secret", userId)))).Identified.ShouldBeFalse();
        (await StartSessionAsync(app, widget.PublicKey, user: new VisitorUserRequest(userId, "not-a-hash"))).Identified.ShouldBeFalse();
        (await StartSessionAsync(app, widget.PublicKey, user: new VisitorUserRequest(userId, Hash(secret, "someone-else")))).Identified.ShouldBeFalse();
        (await StartSessionAsync(app, widget.PublicKey, user: new VisitorUserRequest(userId, Hash(secret, userId).ToUpperInvariant()))).Identified.ShouldBeTrue("hex is hex in either case");

        var rotated = await CreateSecretAsync(app, widget.Id);
        rotated.ShouldNotBe(secret);
        (await StartSessionAsync(app, widget.PublicKey, user: new VisitorUserRequest(userId, Hash(secret, userId)))).Identified.ShouldBeFalse();
        (await StartSessionAsync(app, widget.PublicKey, user: new VisitorUserRequest(userId, Hash(rotated, userId)))).Identified.ShouldBeTrue();

        var dto = await app.Client.GetJsonAsync<WidgetDto>($"/api/widgets/{widget.Id}");
        dto.IdentityVerification.ShouldBeTrue();
        dto.IdentitySecretCreatedAt.ShouldNotBeNull();
        JsonSerializer.Serialize(dto, TestJson.Options).ShouldNotContain(rotated, Case.Sensitive, "the secret is shown only when created");
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task Requiring_verified_users_refuses_everyone_else()
    {
        var app = await fixture.GetAppAsync();
        var widget = await CreateWidgetAsync(app);
        var required = widget.Settings with { Security = widget.Settings.Security with { RequireVerifiedUsers = true } };
        using (var refused = await app.Client.PutJsonAsync($"/api/widgets/{widget.Id}", new SaveWidgetRequest(widget.Name, widget.AgentId, Settings: required)))
        {
            (await refused.ReadProblemAsync(HttpStatusCode.BadRequest)).FieldErrors.ShouldContainKey("settings.security.requireVerifiedUsers");
        }

        var anonymousBefore = await StartSessionAsync(app, widget.PublicKey);
        var secret = await CreateSecretAsync(app, widget.Id);
        using (var saved = await app.Client.PutJsonAsync($"/api/widgets/{widget.Id}", new SaveWidgetRequest(widget.Name, widget.AgentId, Settings: required)))
        {
            (await saved.ReadJsonAsync<WidgetDto>()).Settings.Security.RequireVerifiedUsers.ShouldBeTrue();
        }

        (await app.Client.GetJsonAsync<WidgetPublicConfigDto>($"/api/public/widgets/{widget.PublicKey}/config")).RequireIdentity.ShouldBeTrue();
        using (var anonymous = await app.Client.PostJsonAsync($"/api/public/widgets/{widget.PublicKey}/sessions", new VisitorSessionRequest()))
        {
            (await anonymous.ReadProblemAsync(HttpStatusCode.Forbidden)).Code.ShouldBe("identity_required");
        }

        var userId = ApiClient.UniqueWord("user-");
        using (var wrong = await app.Client.PostJsonAsync($"/api/public/widgets/{widget.PublicKey}/sessions", new VisitorSessionRequest(User: new VisitorUserRequest(userId, Hash("nope", userId)))))
        {
            wrong.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }

        (await GetAsVisitorAsync(app, $"/api/public/widgets/{widget.PublicKey}/conversations/{Guid.NewGuid()}", anonymousBefore.Token)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var verified = await StartSessionAsync(app, widget.PublicKey, user: new VisitorUserRequest(userId, Hash(secret, userId)));
        verified.Identified.ShouldBeTrue();
        (await GetAsVisitorAsync(app, $"/api/public/widgets/{widget.PublicKey}/conversations/{Guid.NewGuid()}", verified.Token)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        using (var off = await app.Client.DeleteAsync($"/api/widgets/{widget.Id}/identity-secret", Ct))
        {
            var dto = await off.ReadJsonAsync<WidgetDto>();
            dto.IdentityVerification.ShouldBeFalse();
            dto.Settings.Security.RequireVerifiedUsers.ShouldBeFalse();
        }

        (await StartSessionAsync(app, widget.PublicKey)).Identified.ShouldBeFalse();
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task Staff_find_people_and_their_conversations_and_admins_export_and_erase_them()
    {
        var app = await fixture.GetAppAsync();
        app.ChatModels.Reset();
        var widget = await CreateWidgetAsync(app);
        var session = await StartSessionAsync(app, widget.PublicKey);
        var conversation = await SendAsync(app, widget.PublicKey, session.Token, "I need help with an invoice");
        var name = ApiClient.UniqueWord("Grace ");
        using (var contact = await PostAsVisitorAsync(app, $"/api/public/widgets/{widget.PublicKey}/contact", session.Token, new VisitorContactRequest(name, "grace@example.com")))
        {
            contact.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        var page = await app.Client.GetJsonAsync<PeoplePageDto>($"/api/people?q={Uri.EscapeDataString(name)}");
        var row = page.Items.ShouldHaveSingleItem();
        row.Name.ShouldBe(name);
        row.Email.ShouldBe("grace@example.com");
        row.ConversationCount.ShouldBe(1);
        row.Widgets.ShouldHaveSingleItem().Id.ShouldBe(widget.Id);
        (await app.Client.GetJsonAsync<PeoplePageDto>($"/api/people?q={Uri.EscapeDataString(name)}&identified=true")).Items.ShouldBeEmpty();
        (await app.Client.GetJsonAsync<PeoplePageDto>($"/api/people?widgetId={widget.Id}")).Items.ShouldContain(item => item.Id == row.Id);
        using (var badCursor = await app.Client.GetAsync("/api/people?cursor=nope&limit=500", Ct))
        {
            (await badCursor.ReadProblemAsync(HttpStatusCode.BadRequest)).FieldErrors.Keys.ShouldBe(["cursor", "limit"], ignoreOrder: true);
        }

        var search = await app.Client.GetJsonAsync<ConversationSearchPageDto>($"/api/conversations/search?contactId={row.Id}");
        var item = search.Items.ShouldHaveSingleItem();
        item.Id.ShouldBe(conversation);
        item.Contact.ShouldBe(new ConversationContactDto(row.Id, name, "grace@example.com", false));
        (await app.Client.GetJsonAsync<ConversationListPageDto>($"/api/conversations?contactId={row.Id}")).Items.ShouldHaveSingleItem().Contact!.Id.ShouldBe(row.Id);
        (await app.Client.GetJsonAsync<ConversationSearchPageDto>($"/api/conversations/search?contactId={Guid.NewGuid()}")).Items.ShouldBeEmpty();
        (await app.Client.GetJsonAsync<ConversationDto>($"/api/conversations/{conversation}")).Contact!.Name.ShouldBe(name);

        using (var patch = await app.Client.PatchJsonAsync($"/api/people/{row.Id}", new UpdatePersonRequest(Notes: "Pays by invoice.", Email: "")))
        {
            var updated = await patch.ReadJsonAsync<PersonDto>();
            updated.Notes.ShouldBe("Pays by invoice.");
            updated.Email.ShouldBeNull();
            updated.SavedName.ShouldBe(name);
        }

        using (var invalid = await app.Client.PatchJsonAsync($"/api/people/{row.Id}", new UpdatePersonRequest(Email: "not an email")))
        {
            (await invalid.ReadProblemAsync(HttpStatusCode.BadRequest)).FieldErrors.ShouldContainKey("email");
        }

        using (var export = await app.Client.GetAsync($"/api/people/{row.Id}/export", Ct))
        {
            var disposition = export.Content.Headers.ContentDisposition.ShouldNotBeNull();
            disposition.DispositionType.ShouldBe("attachment");
            disposition.FileName?.Trim('"').ShouldBe($"person-{row.Id}.json");
            var data = await export.ReadJsonAsync<PersonExportDto>();
            data.Person.Notes.ShouldBe("Pays by invoice.");
            data.Conversations.ShouldHaveSingleItem().Messages.ShouldContain(message => message.Text == "I need help with an invoice");
        }

        using (var unconfirmed = await DeleteWithBodyAsync(app, $"/api/people/{row.Id}", new ErasePersonRequest()))
        {
            (await unconfirmed.ReadProblemAsync(HttpStatusCode.BadRequest)).FieldErrors.ShouldContainKey("confirm");
        }

        using (var erased = await DeleteWithBodyAsync(app, $"/api/people/{row.Id}", new ErasePersonRequest(Confirm: true)))
        {
            (await erased.ReadJsonAsync<ErasePersonResultDto>()).ShouldBe(new ErasePersonResultDto(1, 1));
        }

        (await app.Client.GetAsync($"/api/conversations/{conversation}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await app.Client.GetAsync($"/api/people/{row.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await GetAsVisitorAsync(app, $"/api/public/widgets/{widget.PublicKey}/conversations/{conversation}", session.Token)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized, "the browser went with the person");
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task Portal_conversations_record_the_staff_member_who_started_them()
    {
        var app = await fixture.GetAppAsync();
        app.ChatModels.Reset();
        var stream = await app.ChatAsync(new ChatRequestDto(null, ApiClient.UniqueWord("Hello ")));
        stream.Errors.ShouldBeEmpty(stream.Describe());

        var creator = new ConversationCreatorDto("local-admin", LocalUserName);
        (await app.Client.GetJsonAsync<ConversationDto>($"/api/conversations/{stream.ConversationId}")).CreatedBy.ShouldBe(creator);
        var mine = await app.Client.GetJsonAsync<ConversationSearchPageDto>("/api/conversations/search?createdBy=me&channel=portal&limit=100");
        mine.Items.ShouldContain(item => item.Id == stream.ConversationId && item.CreatedBy == creator);
        (await app.Client.GetJsonAsync<ConversationSearchPageDto>("/api/conversations/search?createdBy=someone-else&channel=portal")).Items.ShouldBeEmpty();
        (await app.Client.GetJsonAsync<ConversationListPageDto>("/api/conversations?createdBy=local-admin&limit=100")).Items.ShouldContain(item => item.Id == stream.ConversationId);
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task Attributes_from_the_page_the_user_and_the_server_follow_the_trust_rules_and_feed_filters_segments_and_the_agent()
    {
        var app = await fixture.GetAppAsync();
        var widget = await CreateWidgetAsync(app);
        var s = ApiClient.UniqueWord("k").ToLowerInvariant().Replace("-", "_", StringComparison.Ordinal);
        var plan = $"plan_{s}";
        var campaign = $"campaign_{s}";
        var userId = ApiClient.UniqueWord("user-");

        using (var off = await PutAsServerAsync(app, widget.PublicKey, userId, "no-secret", new { attributes = new Dictionary<string, object> { [plan] = "pro" } }))
        {
            off.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }

        var secret = await CreateSecretAsync(app, widget.Id);

        using (var started = await app.Client.PostJsonAsync(
            $"/api/public/widgets/{widget.PublicKey}/sessions",
            new VisitorSessionRequest(Attributes: Attributes(new Dictionary<string, object> { [campaign] = "autumn", ["Bad Key"] = 1 }), Page: new VisitorPageRequest("https://shop.example.com/pricing?coupon=SECRET", null, "sk-SK"))))
        {
            var session = await started.ReadJsonAsync<VisitorSessionDto>();
            session.RejectedAttributes.ShouldHaveSingleItem().Key.ShouldBe("Bad Key");
            using var update = await PostAsVisitorAsync(app, $"/api/public/widgets/{widget.PublicKey}/attributes", session.Token, new VisitorAttributesRequest(Attributes(new Dictionary<string, object?> { [campaign] = "winter" })));
            update.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        var anonymous = (await app.Client.GetJsonAsync<PeoplePageDto>($"/api/people?f={campaign}:is:winter")).Items.ShouldHaveSingleItem();
        anonymous.Identified.ShouldBeFalse();
        anonymous.LastPageUrl.ShouldBe("https://shop.example.com/pricing", "the query is not stored");
        anonymous.Language.ShouldBe("sk-SK");
        (await app.Client.GetJsonAsync<PeoplePageDto>("/api/people?limit=100")).Items.ShouldNotContain(item => item.Id == anonymous.Id, "without a filter the list shows people who talked or signed in");
        (await app.Client.GetJsonAsync<PeoplePageDto>("/api/people?all=true&limit=100")).Items.ShouldContain(item => item.Id == anonymous.Id);

        var body = new { name = "Ada", attributes = new Dictionary<string, object> { [plan] = "pro" } };
        (await PutAsServerAsync(app, widget.PublicKey, userId, "wrong-secret", body)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await PutAsServerAsync(app, widget.PublicKey, userId, secret, body, DateTimeOffset.UtcNow.AddMinutes(-10))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        using (var synced = await PutAsServerAsync(app, widget.PublicKey, userId, secret, body))
        {
            var result = await synced.ReadJsonAsync<ServerContactDto>();
            result.Created.ShouldBeTrue();
            result.Applied.ShouldBe([plan]);
        }

        var signedIn = await StartSessionAsync(app, widget.PublicKey, user: new VisitorUserRequest(userId, Hash(secret, userId), Attributes: Attributes(new Dictionary<string, object> { [plan] = "free" })));
        signedIn.Identified.ShouldBeTrue();
        signedIn.RejectedAttributes.ShouldHaveSingleItem().Key.ShouldBe(plan);
        var person = (await app.Client.GetJsonAsync<PeoplePageDto>($"/api/people?f={plan}:is:pro")).Items.ShouldHaveSingleItem();
        person.ExternalId.ShouldBe(userId);
        (await app.Client.GetJsonAsync<PeopleCountDto>($"/api/people/count?f={plan}:set")).Count.ShouldBe(1);
        using (var invalid = await app.Client.GetAsync($"/api/people?f={plan}:gt:1", Ct))
        {
            (await invalid.ReadProblemAsync(HttpStatusCode.BadRequest)).FieldErrors.ShouldContainKey("filter");
        }

        using (var edited = await app.Client.PatchJsonAsync($"/api/people/{person.Id}/attributes", new { attributes = new Dictionary<string, object> { [plan] = "enterprise" } }))
        {
            edited.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        var profile = await app.Client.GetJsonAsync<PersonDto>($"/api/people/{person.Id}");
        profile.Attributes.Single(item => item.Key == plan).ShouldSatisfyAllConditions(item => item.Source.ShouldBe(AttributeSources.Admin), item => item.Value.GetString().ShouldBe("enterprise"));

        var registry = await app.Client.GetJsonAsync<ContactAttributeListDto>("/api/people/attributes");
        registry.Items.Single(item => item.Key == plan).Contacts.ShouldBe(1);
        registry.BuiltIn.ShouldContain(item => item.Key == "returning");
        using (var exposed = await app.Client.PatchJsonAsync($"/api/people/attributes/{plan}", new UpdateContactAttributeRequest("Plan", ExposeToAgent: true)))
        {
            exposed.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        var model = app.ChatModels.Reset();
        var chat = await SendAsync(app, widget.PublicKey, signedIn.Token, "Which features do I have?");
        model.Snapshot().Select(request => request.SystemPrompt()).ShouldContain(prompt => prompt.Contains("# About this visitor", StringComparison.Ordinal) && prompt.Contains("- Plan: enterprise", StringComparison.Ordinal));

        using var created = await app.Client.PostJsonAsync("/api/segments", new SaveSegmentRequest($"Enterprise {s}", Filter: [new ContactCondition(plan, "is", JsonSerializer.SerializeToElement("enterprise"))], Color: "purple"));
        var segment = await created.ReadJsonAsync<SegmentDto>(HttpStatusCode.Created);
        segment.Count.ShouldBe(1);
        (await app.Client.GetJsonAsync<IReadOnlyList<SegmentDto>>("/api/segments")).ShouldContain(item => item.Id == segment.Id);
        (await app.Client.GetJsonAsync<ConversationListPageDto>($"/api/conversations?segmentId={segment.Id}")).Items.Select(item => item.Id).ShouldBe([chat]);
        (await app.Client.GetJsonAsync<ConversationSearchPageDto>($"/api/conversations/search?segmentId={segment.Id}")).Items.Select(item => item.Id).ShouldBe([chat]);
        (await app.Client.GetJsonAsync<PeoplePageDto>($"/api/people?segmentId={segment.Id}")).Items.ShouldHaveSingleItem().Id.ShouldBe(person.Id);
        (await app.Client.GetAsync($"/api/conversations?segmentId={Guid.NewGuid()}", Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var analytics = await app.Client.GetJsonAsync<KnowledgeAgent.Features.Analytics.AnalyticsDto>("/api/analytics?days=7");
        analytics.Segments.Single(item => item.SegmentId == segment.Id).Conversations.ShouldBe(1);

        using (var refused = await DeleteWithBodyAsync(app, $"/api/people/attributes/{plan}", new { confirm = false }))
        {
            refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        using (var deleted = await DeleteWithBodyAsync(app, $"/api/people/attributes/{plan}", new { confirm = true }))
        {
            (await deleted.ReadJsonAsync<DeleteContactAttributeResultDto>()).Contacts.ShouldBe(1);
        }

        (await app.Client.GetJsonAsync<PersonDto>($"/api/people/{person.Id}")).Attributes.ShouldNotContain(item => item.Key == plan);
    }

    private static Task<HttpResponseMessage> PutAsServerAsync<T>(KnowledgeAgentApp app, string key, string userId, string secret, T body, DateTimeOffset? at = null)
    {
        var json = JsonSerializer.Serialize(body, TestJson.Options);
        var timestamp = (at ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds();
        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/public/widgets/{key}/contacts/{Uri.EscapeDataString(userId)}")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-KA-Timestamp", timestamp.ToString(System.Globalization.CultureInfo.InvariantCulture));
        request.Headers.Add("X-KA-Signature", Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{timestamp}.{userId}.{json}"))));
        return app.Client.SendAsync(request, Ct);
    }

    private const string LocalUserName = "Local admin";

    private static Dictionary<string, JsonElement> Attributes(object values) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(JsonSerializer.Serialize(values))!;

    internal static string Hash(string secret, string userId) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(userId)));

    private static async Task<PersonDto> PersonOfAsync(KnowledgeAgentApp app, Guid conversationId)
    {
        var conversation = await app.Client.GetJsonAsync<ConversationDto>($"/api/conversations/{conversationId}");
        return await app.Client.GetJsonAsync<PersonDto>($"/api/people/{conversation.ContactId.ShouldNotBeNull()}");
    }

    private static async Task<WidgetDto> CreateWidgetAsync(KnowledgeAgentApp app)
    {
        using var agentResponse = await app.Client.PostJsonAsync(
            "/api/agents",
            new SaveAgentRequest("Shop assistant", ApiClient.UniqueWord("shop-"), Tools: [ToolNames.SearchKnowledgeBase]));
        var agent = await agentResponse.ReadJsonAsync<AgentDto>(HttpStatusCode.Created);
        using var response = await app.Client.PostJsonAsync("/api/widgets", new SaveWidgetRequest("Shop help", agent.Id));
        return await response.ReadJsonAsync<WidgetDto>(HttpStatusCode.Created);
    }

    private static async Task<string> CreateSecretAsync(KnowledgeAgentApp app, Guid widgetId)
    {
        using var response = await app.Client.PostAsync($"/api/widgets/{widgetId}/identity-secret", content: null, Ct);
        var created = await response.ReadJsonAsync<WidgetIdentitySecretDto>();
        created.Secret.ShouldStartWith("ka_ivs_");
        return created.Secret;
    }

    private static async Task<VisitorSessionDto> StartSessionAsync(KnowledgeAgentApp app, string key, string? token = null, VisitorUserRequest? user = null)
    {
        using var response = await app.Client.PostJsonAsync($"/api/public/widgets/{key}/sessions", new VisitorSessionRequest(token, user));
        return await response.ReadJsonAsync<VisitorSessionDto>();
    }

    private static async Task<Guid> SendAsync(KnowledgeAgentApp app, string key, string token, string message)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/public/widgets/{key}/messages")
        {
            Content = JsonContent.Create(new VisitorMessageRequest(message), options: TestJson.Options),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(SseReader.AcceptHeader);
        using var response = await app.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        var events = await SseReader.ReadAllAsync(response, Ct);
        var stream = new ChatStream(response.Content.Headers.ContentType?.MediaType, null, null, events);
        stream.Errors.ShouldBeEmpty(stream.Describe());
        return stream.ConversationId;
    }

    private static async Task<HttpResponseMessage> PostAsVisitorAsync<T>(KnowledgeAgentApp app, string url, string token, T body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body, options: TestJson.Options) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await app.Client.SendAsync(request, Ct);
    }

    private static async Task<HttpResponseMessage> GetAsVisitorAsync(KnowledgeAgentApp app, string url, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await app.Client.SendAsync(request, Ct);
    }

    private static Task<HttpResponseMessage> DeleteWithBodyAsync<T>(KnowledgeAgentApp app, string url, T body)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, url) { Content = JsonContent.Create(body, options: TestJson.Options) };
        return app.Client.SendAsync(request, Ct);
    }
}

public sealed class InMemoryPeopleScenarios(InMemoryAppFixture fixture) : PeopleScenarios(fixture), IClassFixture<InMemoryAppFixture>;

public sealed class ContainersPeopleScenarios(ContainersAppFixture fixture) : PeopleScenarios(fixture), IClassFixture<ContainersAppFixture>;
