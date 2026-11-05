using System.Text.Json;
using KnowledgeAgent.Features.Chat;
using KnowledgeAgent.Features.Feedback;
using KnowledgeAgent.Features.Handover;
using KnowledgeAgent.Features.People;
using KnowledgeAgent.Features.Widgets;
using KnowledgeAgent.Infrastructure.Persistence;
using KnowledgeAgent.Tests.Infrastructure.Persistence.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using FeedbackItem = KnowledgeAgent.Features.Feedback.Feedback;
using HandoverRequest = KnowledgeAgent.Features.Handover.Handover;

namespace KnowledgeAgent.Tests.Features.People;

// The database is shared per class, so every test uses widgets and people of its own.
public abstract class PeopleRepositoryContractTests<TFixture>
    where TFixture : PersistenceFixture
{
    private static readonly DateTimeOffset Now = new(2099, 6, 1, 12, 0, 0, TimeSpan.Zero);

    protected PeopleRepositoryContractTests(TFixture fixture)
    {
        _ = fixture.Services;
        Fixture = fixture;
    }

    protected TFixture Fixture { get; }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_visitor_gets_an_anonymous_contact_with_its_details_and_earlier_conversations()
    {
        var widget = await AddWidgetAsync();
        var visitor = await AddVisitorAsync(widget, name: "Jane");
        var earlier = await AddConversationAsync(widget, visitor, contactId: null);

        var contactId = await InScopeAsync(people => people.EnsureForVisitorAsync(visitor.Id, Now, Ct));
        (await InScopeAsync(people => people.EnsureForVisitorAsync(visitor.Id, Now.AddMinutes(1), Ct))).ShouldBe(contactId, "a visitor keeps its contact");

        var contact = (await InScopeAsync(people => people.GetAsync(contactId, Ct))).ShouldNotBeNull();
        contact.Name.ShouldBe("Jane");
        contact.Identified.ShouldBeFalse();
        contact.ConversationCount.ShouldBe(1);
        contact.LastSeenAt.ShouldBe(Now.AddMinutes(1));
        (await ReadAsync(db => db.Conversations.SingleAsync(item => item.Id == earlier, Ct))).ContactId.ShouldBe(contactId);

        await InScopeAsync(people => people.SaveVisitorDetailsAsync(visitor.Id, null, "jane@example.com", Now, Ct));
        (await InScopeAsync(people => people.GetAsync(contactId, Ct)))!.Email.ShouldBe("jane@example.com");
        Contact.DisplayNameOf(Guid.Parse("0199c1e2-0000-7000-8000-000000004f2a"), null).ShouldBe("Visitor 4F2A");
    }

    [Fact]
    public async Task Identifying_merges_the_anonymous_history_of_the_browser_into_the_user()
    {
        var widget = await AddWidgetAsync();
        var otherWidget = await AddWidgetAsync();
        var userId = $"user-{Guid.NewGuid():N}";
        var phone = await AddVisitorAsync(otherWidget);
        await InScopeAsync(people => people.IdentifyAsync(phone.Id, User(userId, "Ada", attributes: new() { ["plan"] = "free" }), Now.AddDays(-3), Ct));
        var laptop = await AddVisitorAsync(widget);
        var anonymousId = await InScopeAsync(people => people.EnsureForVisitorAsync(laptop.Id, Now.AddDays(-2), Ct));
        var anonymousChat = await AddConversationAsync(widget, laptop, anonymousId);
        var anonymous = (await InScopeAsync(people => people.GetAsync(anonymousId, Ct))).ShouldNotBeNull();
        anonymous.Notes = "Asked about refunds.";
        await InScopeAsync(people => people.UpdateAsync(anonymous, Ct));

        var user = await IdentifyAsync(people => people.IdentifyAsync(laptop.Id, User(userId, null, "ada@example.com", new() { ["company"] = "Analytical" }), Now, Ct));

        user.ExternalId.ShouldBe(userId);
        user.Name.ShouldBe("Ada");
        user.Email.ShouldBe("ada@example.com");
        user.Attributes.Keys.ShouldBe(["plan", "company"], ignoreOrder: true);
        user.Attributes["company"].GetString().ShouldBe("Analytical");
        user.AttributeSources["plan"].Source.ShouldBe(AttributeSources.Verified);
        user.Notes.ShouldBe("Asked about refunds.");
        user.ConversationCount.ShouldBe(1);
        (await InScopeAsync(people => people.GetAsync(anonymousId, Ct))).ShouldBeNull("the anonymous contact had no other browser");
        (await ReadAsync(db => db.Conversations.SingleAsync(item => item.Id == anonymousChat, Ct))).ContactId.ShouldBe(user.Id);
        var visitor = await ReadAsync(db => db.WidgetVisitors.SingleAsync(item => item.Id == laptop.Id, Ct));
        visitor.ContactId.ShouldBe(user.Id);
        visitor.Email.ShouldBe("ada@example.com", "replies by email reach the user");

        var profile = (await InScopeAsync(people => people.GetProfileAsync(user.Id, Ct))).ShouldNotBeNull();
        profile.Widgets.Select(item => item.Id).ShouldBe([widget.Id, otherWidget.Id], ignoreOrder: true);
        profile.Stats.Conversations.ShouldBe(1);
    }

    [Fact]
    public async Task The_list_searches_filters_by_widget_and_sign_in_and_pages_by_last_seen()
    {
        var widget = await AddWidgetAsync();
        var marker = Guid.NewGuid().ToString("N")[..8];
        var ids = new List<Guid>();
        for (var index = 0; index < 3; index++)
        {
            var visitor = await AddVisitorAsync(widget, name: $"Person {marker} {index}");
            ids.Add(await InScopeAsync(people => people.EnsureForVisitorAsync(visitor.Id, Now.AddMinutes(index), Ct)));
        }

        var signedIn = await AddVisitorAsync(widget);
        var user = await IdentifyAsync(people => people.IdentifyAsync(signedIn.Id, User($"{marker}-user", $"Signed {marker}"), Now.AddMinutes(10), Ct));

        var first = await InScopeAsync(people => people.ListAsync(new PeopleQuery { Text = marker, Limit = 2 }, Ct));
        first.Items.Select(item => item.Id).ShouldBe([user.Id, ids[2]]);
        first.Items[0].Identified.ShouldBeTrue();
        first.Items[0].Widgets.ShouldHaveSingleItem().Name.ShouldBe(widget.Name);
        first.NextCursor.ShouldNotBeNull();
        Conversations_cursor(first.NextCursor, out var after);
        var second = await InScopeAsync(people => people.ListAsync(new PeopleQuery { Text = marker, Limit = 2, After = after }, Ct));
        second.Items.Select(item => item.Id).ShouldBe([ids[1], ids[0]]);
        second.NextCursor.ShouldBeNull();

        (await InScopeAsync(people => people.ListAsync(new PeopleQuery { Text = marker, Identified = true }, Ct))).Items.Select(item => item.Id).ShouldBe([user.Id]);
        (await InScopeAsync(people => people.ListAsync(new PeopleQuery { Text = marker.ToUpperInvariant(), Identified = false }, Ct))).Items.Count.ShouldBe(3);
        (await InScopeAsync(people => people.ListAsync(new PeopleQuery { Text = $"{marker}-user" }, Ct))).Items.ShouldHaveSingleItem().Id.ShouldBe(user.Id);
        (await InScopeAsync(people => people.ListAsync(new PeopleQuery { Text = marker, WidgetId = Guid.NewGuid() }, Ct))).Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task Erasure_deletes_the_person_with_browsers_conversations_feedback_and_handovers_and_export_has_them_first()
    {
        var widget = await AddWidgetAsync();
        var visitor = await AddVisitorAsync(widget, name: "Grace", email: "grace@example.com");
        var contactId = await InScopeAsync(people => people.EnsureForVisitorAsync(visitor.Id, Now, Ct));
        var conversation = await AddConversationAsync(widget, visitor, contactId);
        var bystander = await AddVisitorAsync(widget);
        var kept = await AddConversationAsync(widget, bystander, contactId: null);
        await SeedAsync(db =>
        {
            db.ConversationMessages.Add(new ConversationMessage { Id = Guid.CreateVersion7(), ConversationId = conversation, Sequence = 1, Author = MessageAuthor.Visitor, Text = "My order number is 42.", CreatedAt = Now });
            db.Feedback.Add(new FeedbackItem { ConversationId = conversation, Kind = FeedbackKind.Conversation, Rating = 4, Author = FeedbackAuthor.Visitor, AuthorKey = visitor.Id.ToString(), CreatedAt = Now, UpdatedAt = Now });
            db.Handovers.Add(new HandoverRequest
            {
                ConversationId = conversation,
                Channel = "portal",
                Status = HandoverStatus.Resolved,
                Source = HandoverSource.Visitor,
                Token = Guid.NewGuid().ToString("N")[..HandoverRequest.TokenLength],
                RequestedAt = Now,
                UpdatedAt = Now,
            });
        });

        var profile = (await InScopeAsync(people => people.GetProfileAsync(contactId, Ct))).ShouldNotBeNull();
        profile.Stats.ShouldBe(new PersonStatsDto(1, 1, 4, 1, Now));
        var export = (await InScopeAsync(people => people.ExportAsync(contactId, Now, Ct))).ShouldNotBeNull();
        export.Browsers.ShouldHaveSingleItem().Email.ShouldBe("grace@example.com");
        var exported = export.Conversations.ShouldHaveSingleItem();
        exported.Messages.ShouldHaveSingleItem().Text.ShouldBe("My order number is 42.");
        exported.Feedback.ShouldHaveSingleItem().Rating.ShouldBe(4);
        exported.Handovers.ShouldHaveSingleItem().Status.ShouldBe("resolved");

        var erased = await InScopeAsync(people => people.EraseAsync(contactId, Ct));

        erased.ShouldBe(new ErasePersonResultDto(1, 1));
        (await ReadAsync(db => db.Contacts.AnyAsync(item => item.Id == contactId, Ct))).ShouldBeFalse();
        (await ReadAsync(db => db.WidgetVisitors.AnyAsync(item => item.Id == visitor.Id, Ct))).ShouldBeFalse();
        (await ReadAsync(db => db.Conversations.AnyAsync(item => item.Id == conversation, Ct))).ShouldBeFalse();
        (await ReadAsync(db => db.ConversationMessages.AnyAsync(item => item.ConversationId == conversation, Ct))).ShouldBeFalse();
        (await ReadAsync(db => db.Feedback.AnyAsync(item => item.ConversationId == conversation, Ct))).ShouldBeFalse();
        (await ReadAsync(db => db.Handovers.AnyAsync(item => item.ConversationId == conversation, Ct))).ShouldBeFalse();
        (await ReadAsync(db => db.Conversations.AnyAsync(item => item.Id == kept, Ct))).ShouldBeTrue("other people's conversations stay");
        (await InScopeAsync(people => people.EraseAsync(contactId, Ct))).ShouldBeNull();
    }

    [Fact]
    public async Task Tidying_recounts_conversations_and_deletes_anonymous_people_nothing_is_left_of()
    {
        var widget = await AddWidgetAsync();
        var gone = await AddVisitorAsync(widget);
        var goneId = await InScopeAsync(people => people.EnsureForVisitorAsync(gone.Id, Now, Ct));
        var signedIn = await AddVisitorAsync(widget);
        var user = await IdentifyAsync(people => people.IdentifyAsync(signedIn.Id, User($"user-{Guid.NewGuid():N}", "Ada"), Now, Ct));
        await AddConversationAsync(widget, signedIn, user.Id);
        await SeedAsync(db =>
        {
            db.WidgetVisitors.RemoveRange(db.WidgetVisitors.Where(item => item.Id == gone.Id || item.Id == signedIn.Id));
            db.Contacts.Where(item => item.Id == user.Id).ToList().ForEach(item => item.ConversationCount = 7);
        });

        (await InScopeAsync(people => people.TidyAsync(Ct))).ShouldBeGreaterThanOrEqualTo(1);

        (await InScopeAsync(people => people.GetAsync(goneId, Ct))).ShouldBeNull();
        var kept = (await InScopeAsync(people => people.GetAsync(user.Id, Ct))).ShouldNotBeNull("identified people stay, as the website may bring them back");
        kept.ConversationCount.ShouldBe(1);
    }

    [Fact]
    public async Task Conversations_keep_their_person_count_as_they_come_and_go()
    {
        var widget = await AddWidgetAsync();
        var visitor = await AddVisitorAsync(widget);
        var contactId = await InScopeAsync(people => people.EnsureForVisitorAsync(visitor.Id, Now, Ct));

        await using (var scope = Fixture.Services.CreateAsyncScope())
        {
            var conversations = scope.ServiceProvider.GetRequiredService<IConversationRepository>();
            var created = await conversations.CreateAsync(
                new Conversation { Channel = ConversationChannel.Widget, WidgetId = widget.Id, VisitorId = visitor.Id, ContactId = contactId, CreatedAt = Now, UpdatedAt = Now },
                Ct);
            (await InScopeAsync(people => people.GetAsync(contactId, Ct)))!.ConversationCount.ShouldBe(1);
            (await conversations.FindLatestForContactAsync(contactId, widget.Id, Ct)).ShouldBe(created.Id);
            (await conversations.FindLatestForContactAsync(contactId, Guid.NewGuid(), Ct)).ShouldBeNull();

            (await conversations.DeleteAsync(created.Id, Ct)).ShouldBeTrue();
        }

        (await InScopeAsync(people => people.GetAsync(contactId, Ct)))!.ConversationCount.ShouldBe(0);
    }

    [Fact]
    public async Task Attribute_filters_work_the_same_for_every_type_on_both_stores()
    {
        var s = Guid.NewGuid().ToString("N")[..6];
        var widget = await AddWidgetAsync();
        var c1 = await WithAttributesAsync(widget, null, new() { [$"plan_{s}"] = "pro", [$"seats_{s}"] = 5, [$"beta_{s}"] = true, [$"renews_{s}"] = "2099-12-01", [$"joined_{s}"] = Now.AddDays(-3).ToString("O") });
        var c2 = await WithAttributesAsync(widget, null, new() { [$"plan_{s}"] = "free", [$"seats_{s}"] = 12, [$"beta_{s}"] = false, [$"renews_{s}"] = "2100-03-01", [$"joined_{s}"] = Now.AddDays(-30).ToString("O") });
        var c3 = await WithAttributesAsync(widget, null, new() { [$"plan_{s}"] = "Pro team" });
        var jane = await AddVisitorAsync(widget, name: "Jane");
        var c4 = await InScopeAsync(people => people.EnsureForVisitorAsync(jane.Id, Now, Ct));

        async Task<Guid[]> Matching(params string[] f)
        {
            var registry = await InScopeAsync(people => people.AttributeRegistryAsync(Ct));
            ContactFilters.TryParse(null, [.. f, $"widget:is:{widget.Id}"], out var parsed, out var error).ShouldBeTrue(error);
            var errors = new List<string>();
            var conditions = ContactFilters.Resolve(parsed, registry, strict: true, errors);
            errors.ShouldBeEmpty();
            var query = new PeopleQuery { Conditions = conditions, Now = Now };
            var page = await InScopeAsync(people => people.ListAsync(query, Ct));
            (await InScopeAsync(people => people.CountAsync(query, Ct))).ShouldBe(page.Items.Count, "the count matches the list");
            return [.. page.Items.Select(item => item.Id).Order()];
        }

        Guid[] Of(params Guid[] ids) => [.. ids.Order()];

        (await Matching($"plan_{s}:is:pro")).ShouldBe(Of(c1), "is is exact for custom text");
        (await Matching($"plan_{s}:is_not:pro")).ShouldBe(Of(c2, c3, c4), "is not includes people without the key");
        (await Matching($"plan_{s}:contains:PRO")).ShouldBe(Of(c1, c3), "contains ignores case");
        (await Matching($"plan_{s}:set")).ShouldBe(Of(c1, c2, c3));
        (await Matching($"plan_{s}:not_set")).ShouldBe(Of(c4));
        (await Matching($"seats_{s}:gt:6")).ShouldBe(Of(c2));
        (await Matching($"seats_{s}:lt:6")).ShouldBe(Of(c1));
        (await Matching($"seats_{s}:eq:5")).ShouldBe(Of(c1));
        (await Matching($"seats_{s}:neq:5")).ShouldBe(Of(c2, c3, c4));
        (await Matching($"seats_{s}:between:5..12")).ShouldBe(Of(c1, c2));
        (await Matching($"beta_{s}:true")).ShouldBe(Of(c1));
        (await Matching($"beta_{s}:false")).ShouldBe(Of(c2));
        (await Matching($"renews_{s}:before:2100-01-01")).ShouldBe(Of(c1));
        (await Matching($"renews_{s}:after:2100-01-01")).ShouldBe(Of(c2));
        (await Matching($"joined_{s}:within_days:7")).ShouldBe(Of(c1));
        (await Matching($"seats_{s}:gt:1", $"plan_{s}:is:free")).ShouldBe(Of(c2), "conditions all apply");
        (await Matching("name:is:jane")).ShouldBe(Of(c4), "built-in text ignores case");
        (await Matching("name:contains:AN")).ShouldBe(Of(c4));
        (await Matching("identified:false")).ShouldBe(Of(c1, c2, c3, c4));
        (await Matching("sessions:gt:0", "returning:false")).ShouldBe(Of(c1, c2, c3, c4));
        (await Matching("last_seen:within_days:1")).ShouldBe(Of(c1, c2, c3, c4));
        (await Matching("first_seen:after:2100-01-01")).ShouldBeEmpty();

        var list = await InScopeAsync(people => people.ListAsync(new PeopleQuery { WidgetId = widget.Id, Limit = 100 }, Ct));
        list.Items.Single(item => item.Id == c1).Attributes[$"seats_{s}"].GetDecimal().ShouldBe(5);
        var active = await InScopeAsync(people => people.ListAsync(new PeopleQuery { WidgetId = widget.Id, ActiveOnly = true }, Ct));
        active.Items.ShouldBeEmpty("nobody here talked yet or signed in");
    }

    [Fact]
    public async Task Attributes_from_the_browser_make_a_contact_and_sessions_roll_up_from_the_visitor()
    {
        var s = Guid.NewGuid().ToString("N")[..6];
        var widget = await AddWidgetAsync();
        var visitor = new WidgetVisitor { WidgetId = widget.Id, CreatedAt = Now.AddDays(-2), LastSeenAt = Now, SessionCount = 3, LastPageUrl = "https://shop.example.com/pricing", Language = "de-AT" };
        await SeedAsync(db => db.WidgetVisitors.Add(visitor));

        (await InScopeAsync(people => people.SetVisitorAttributesAsync(visitor.Id, Values(new() { ["Bad Key"] = "x" }), AttributeSources.Browser, Now, Ct))).Rejected.ShouldHaveSingleItem();
        (await ReadAsync(db => db.WidgetVisitors.SingleAsync(item => item.Id == visitor.Id, Ct))).ContactId.ShouldBeNull("nothing was stored, so there is no contact");

        var result = await InScopeAsync(people => people.SetVisitorAttributesAsync(visitor.Id, Values(new() { [$"campaign_{s}"] = "autumn" }), AttributeSources.Browser, Now, Ct));
        result.Applied.ShouldBe([$"campaign_{s}"]);
        var contactId = (await ReadAsync(db => db.WidgetVisitors.SingleAsync(item => item.Id == visitor.Id, Ct))).ContactId.ShouldNotBeNull();
        var contact = (await InScopeAsync(people => people.GetAsync(contactId, Ct))).ShouldNotBeNull();
        contact.SessionCount.ShouldBe(3);
        contact.LastPageUrl.ShouldBe("https://shop.example.com/pricing");
        contact.Language.ShouldBe("de-AT");
        contact.Returning.ShouldBeTrue();
        contact.AttributeSources[$"campaign_{s}"].Source.ShouldBe(AttributeSources.Browser);

        await InScopeAsync(people => people.RecordVisitAsync(contactId, new VisitorVisit(true, "https://shop.example.com/cart", null, null), Now.AddMinutes(10), Ct));
        (await InScopeAsync(people => people.GetAsync(contactId, Ct)))!.SessionCount.ShouldBe(3, "ten minutes is the same session");
        await InScopeAsync(people => people.RecordVisitAsync(contactId, new VisitorVisit(true, "https://shop.example.com/cart", null, null), Now.AddHours(2), Ct));
        var visited = (await InScopeAsync(people => people.GetAsync(contactId, Ct)))!;
        visited.SessionCount.ShouldBe(4);
        visited.LastPageUrl.ShouldBe("https://shop.example.com/cart");

        // The website's server signs in the same user later: its values win over the browser's for good.
        var userId = $"user-{s}";
        var server = await InScopeAsync(people => people.UpsertFromServerAsync(userId, "Ada", null, Values(new() { [$"campaign_{s}"] = "spring", [$"mrr_{s}"] = 99 }), Now, Ct));
        server.Created.ShouldBeTrue();
        (await InScopeAsync(people => people.UpsertFromServerAsync(userId, null, "ada@example.com", Values([]), Now, Ct))).ShouldBe(new ServerContactResult(server.ContactId, false, AttributeChangeResult.None));
        var user = await IdentifyAsync(people => people.IdentifyAsync(visitor.Id, User(userId, null, attributes: new() { [$"mrr_{s}"] = 1 }), Now.AddHours(3), Ct));
        user.Id.ShouldBe(server.ContactId);
        user.Attributes[$"campaign_{s}"].GetString().ShouldBe("spring", "the browser value did not overwrite the server's when the histories merged");
        user.Attributes[$"mrr_{s}"].GetDecimal().ShouldBe(99, "identify() cannot overwrite a server value");
        user.Email.ShouldBe("ada@example.com");
        user.SessionCount.ShouldBeGreaterThan(4);
    }

    [Fact]
    public async Task The_registry_counts_renames_and_deletes_keys_everywhere()
    {
        var s = Guid.NewGuid().ToString("N")[..6];
        var widget = await AddWidgetAsync();
        var first = await WithAttributesAsync(widget, null, new() { [$"tier_{s}"] = "gold", [$"seats_{s}"] = 2 });
        await WithAttributesAsync(widget, null, new() { [$"tier_{s}"] = "gold" });
        await WithAttributesAsync(widget, null, new() { [$"tier_{s}"] = "silver" });

        var listed = (await InScopeAsync(people => people.ListAttributesAsync(Ct))).Single(item => item.Key == $"tier_{s}");
        listed.Contacts.ShouldBe(3);
        listed.Type.ShouldBe(AttributeTypes.String);
        (await InScopeAsync(people => people.AttributeValuesAsync($"tier_{s}", 5, Ct))).ShouldBe([new AttributeValueCountDto("gold", 2), new AttributeValueCountDto("silver", 1)]);

        var renamed = await InScopeAsync(people => people.UpdateAttributeAsync($"tier_{s}", new UpdateContactAttributeRequest("Customer tier", Visible: false, ExposeToAgent: true), Ct));
        renamed!.Label.ShouldBe("Customer tier");
        (await InScopeAsync(people => people.AgentFactsAsync(first, Ct))).ShouldBe([new KnowledgeAgent.Features.Chat.VisitorFact("Customer tier", "gold", true)]);
        var profile = (await InScopeAsync(people => people.GetProfileAsync(first, Ct)))!;
        profile.Attributes.Single(item => item.Key == $"tier_{s}").ShouldSatisfyAllConditions(item => item.Label.ShouldBe("Customer tier"), item => item.Visible.ShouldBeFalse());

        (await InScopeAsync(people => people.DeleteAttributeAsync($"tier_{s}", Ct))).ShouldBe(3);
        (await InScopeAsync(people => people.DeleteAttributeAsync($"tier_{s}", Ct))).ShouldBeNull();
        var after = (await InScopeAsync(people => people.GetAsync(first, Ct)))!;
        after.Attributes.Keys.ShouldBe([$"seats_{s}"]);
        after.AttributeSources.Keys.ShouldBe([$"seats_{s}"]);
        (await InScopeAsync(people => people.AttributeRegistryAsync(Ct))).ShouldNotContainKey($"tier_{s}");
    }

    [Fact]
    public async Task Segments_count_their_members_and_narrow_conversations_and_analytics()
    {
        var s = Guid.NewGuid().ToString("N")[..6];
        var widget = await AddWidgetAsync();
        var pro = await WithAttributesAsync(widget, null, new() { [$"plan_{s}"] = "pro" });
        var proToo = await WithAttributesAsync(widget, null, new() { [$"plan_{s}"] = "pro" });
        var free = await WithAttributesAsync(widget, null, new() { [$"plan_{s}"] = "free" });
        var visitors = await ReadAsync(db => db.WidgetVisitors.Where(item => item.WidgetId == widget.Id).ToDictionaryAsync(item => item.ContactId!.Value, Ct));
        var proChat = await AddConversationAsync(widget, visitors[pro], pro);
        var proTooChat = await AddConversationAsync(widget, visitors[proToo], proToo);
        var freeChat = await AddConversationAsync(widget, visitors[free], free);
        await SeedAsync(db =>
        {
            db.Handovers.Add(new HandoverRequest
            {
                ConversationId = proChat,
                Channel = "portal",
                Status = HandoverStatus.Resolved,
                Source = HandoverSource.Visitor,
                Token = Guid.NewGuid().ToString("N")[..HandoverRequest.TokenLength],
                RequestedAt = Now,
                UpdatedAt = Now,
            });
            db.Feedback.Add(new FeedbackItem { ConversationId = proTooChat, Kind = FeedbackKind.Conversation, Rating = 4, Author = FeedbackAuthor.Visitor, AuthorKey = "x", CreatedAt = Now, UpdatedAt = Now });
        });

        await using var scope = Fixture.Services.CreateAsyncScope();
        var segments = scope.ServiceProvider.GetRequiredService<ISegmentService>();
        var admin = new SegmentViewer("admin-1", "Admin", Admin: true);
        var support = new SegmentViewer("support-1", "Sam", Admin: false);
        var filter = new List<ContactCondition> { new($"plan_{s}", "is", JsonSerializer.SerializeToElement("pro")) };

        var shared = await segments.CreateAsync(new SaveSegmentRequest($"Pro {s}", "Paying", filter, "teal", "star"), admin, Ct);
        shared.Count.ShouldBe(2);
        shared.Mine.ShouldBeTrue();
        var mine = await segments.CreateAsync(new SaveSegmentRequest($"Mine {s}", Filter: filter, Shared: false), support, Ct);
        (await segments.ListAsync(new SegmentViewer("support-2", "Kim", false), Ct)).Select(item => item.Id).ShouldContain(shared.Id);
        (await segments.ListAsync(new SegmentViewer("support-2", "Kim", false), Ct)).Select(item => item.Id).ShouldNotContain(mine.Id, "private segments are their creator's");
        (await segments.FindAsync(mine.Id, admin, Ct)).ShouldNotBeNull("admins see every segment");
        (await segments.UpdateAsync(mine.Id, new SaveSegmentRequest(Name: "Taken"), new SegmentViewer("support-2", "Kim", false), Ct)).ShouldBeNull();
        await Should.ThrowAsync<KnowledgeAgent.Infrastructure.Web.ValidationFailedException>(() =>
            segments.CreateAsync(new SaveSegmentRequest("Bad", Filter: [new($"plan_{s}", "gt", JsonSerializer.SerializeToElement(1))]), admin, Ct));

        var search = scope.ServiceProvider.GetRequiredService<KnowledgeAgent.Features.Conversations.IConversationSearch>();
        var conditions = await segments.ConditionsAsync((await segments.FindAsync(shared.Id, admin, Ct))!, Ct);
        var inSegment = await search.ListAsync(new KnowledgeAgent.Features.Conversations.ConversationSearchQuery { WidgetId = widget.Id, ContactConditions = conditions, Now = Now }, Ct);
        inSegment.Items.Select(item => item.Id).ShouldBe([proChat, proTooChat], ignoreOrder: true);
        var searched = await search.SearchAsync(new KnowledgeAgent.Features.Conversations.ConversationSearchQuery { WidgetId = widget.Id, ContactConditions = conditions, Now = Now }, Ct);
        searched.Items.Select(item => item.Id).ShouldNotContain(freeChat);

        var analytics = scope.ServiceProvider.GetRequiredService<KnowledgeAgent.Features.Analytics.IAnalyticsRepository>();
        var breakdown = (await analytics.SegmentsAsync(Now, 7, Ct)).Single(item => item.SegmentId == shared.Id);
        breakdown.ShouldBe(new KnowledgeAgent.Features.Analytics.AnalyticsSegmentDto(shared.Id, $"Pro {s}", "teal", 2, 2, 1, 0.5, 1, 4));

        var changed = await segments.UpdateAsync(shared.Id, new SaveSegmentRequest(Filter: [new($"plan_{s}", "is", JsonSerializer.SerializeToElement("free"))]), support, Ct);
        changed!.Count.ShouldBe(1, "a changed filter is counted again at once");
        (await segments.DeleteAsync(shared.Id, Ct)).ShouldBeTrue();
        (await segments.DeleteAsync(shared.Id, Ct)).ShouldBeFalse();
    }

    [Fact]
    public async Task Tidying_unlinks_contacts_that_were_only_page_opens_and_keeps_their_browsers()
    {
        var widget = await AddWidgetAsync();
        var opened = await AddVisitorAsync(widget);
        var emptyId = await InScopeAsync(people => people.EnsureForVisitorAsync(opened.Id, DateTimeOffset.UtcNow.AddDays(-3), Ct));
        await SeedAsync(db => db.Contacts.Where(item => item.Id == emptyId).ToList().ForEach(item => item.LastSeenAt = DateTimeOffset.UtcNow.AddDays(-3)));
        var named = await AddVisitorAsync(widget, name: "Kept");
        var namedId = await InScopeAsync(people => people.EnsureForVisitorAsync(named.Id, DateTimeOffset.UtcNow.AddDays(-3), Ct));
        await SeedAsync(db => db.Contacts.Where(item => item.Id == namedId).ToList().ForEach(item => item.LastSeenAt = DateTimeOffset.UtcNow.AddDays(-3)));

        await InScopeAsync(people => people.TidyAsync(Ct));

        (await InScopeAsync(people => people.GetAsync(emptyId, Ct))).ShouldBeNull();
        var browser = await ReadAsync(db => db.WidgetVisitors.SingleOrDefaultAsync(item => item.Id == opened.Id, Ct));
        browser.ShouldNotBeNull("the browser stays a visitor");
        browser.ContactId.ShouldBeNull();
        (await InScopeAsync(people => people.GetAsync(namedId, Ct))).ShouldNotBeNull("a contact with a name stays");
    }

    private async Task<Guid> WithAttributesAsync(Widget widget, string? name, Dictionary<string, object?> attributes)
    {
        var visitor = await AddVisitorAsync(widget, name);
        var result = await InScopeAsync(people => people.SetVisitorAttributesAsync(visitor.Id, Values(attributes), AttributeSources.Verified, Now, Ct));
        result.Rejected.ShouldBeEmpty();
        return (await ReadAsync(db => db.WidgetVisitors.SingleAsync(item => item.Id == visitor.Id, Ct))).ContactId!.Value;
    }

    private static void Conversations_cursor(string? text, out KnowledgeAgent.Features.Conversations.ConversationCursor? cursor) =>
        KnowledgeAgent.Features.Conversations.ConversationCursor.TryDecode(text, out cursor).ShouldBeTrue();

    private static VerifiedUser User(string userId, string? name, string? email = null, Dictionary<string, object?>? attributes = null) =>
        new(userId, name, email, Values(attributes ?? []));

    private static Dictionary<string, JsonElement> Values(Dictionary<string, object?> values) =>
        values.ToDictionary(entry => entry.Key, entry => JsonSerializer.SerializeToElement(entry.Value), StringComparer.Ordinal);

    private async Task<Contact> IdentifyAsync(Func<IPeopleRepository, Task<IdentifiedContact>> action) => (await InScopeAsync(action)).Contact;

    private async Task<Widget> AddWidgetAsync()
    {
        var widget = new Widget { PublicKey = $"wk_{Guid.NewGuid():N}"[..27], Name = $"Widget {Guid.NewGuid():N}"[..20], AgentId = Guid.NewGuid(), CreatedAt = Now, UpdatedAt = Now };
        await SeedAsync(db => db.Widgets.Add(widget));
        return widget;
    }

    private async Task<WidgetVisitor> AddVisitorAsync(Widget widget, string? name = null, string? email = null)
    {
        var visitor = new WidgetVisitor { WidgetId = widget.Id, Name = name, Email = email, CreatedAt = Now.AddDays(-1), LastSeenAt = Now };
        await SeedAsync(db => db.WidgetVisitors.Add(visitor));
        return visitor;
    }

    private async Task<Guid> AddConversationAsync(Widget widget, WidgetVisitor visitor, Guid? contactId)
    {
        var conversation = new Conversation { Channel = ConversationChannel.Widget, WidgetId = widget.Id, VisitorId = visitor.Id, ContactId = contactId, CreatedAt = Now, UpdatedAt = Now };
        await SeedAsync(db => db.Conversations.Add(conversation));
        return conversation.Id;
    }

    private async Task SeedAsync(Action<KnowledgeDbContext> seed)
    {
        await using var scope = Fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<KnowledgeDbContext>();
        seed(db);
        await db.SaveChangesAsync(Ct);
    }

    private async Task<T> ReadAsync<T>(Func<KnowledgeDbContext, Task<T>> read)
    {
        await using var scope = Fixture.Services.CreateAsyncScope();
        return await read(scope.ServiceProvider.GetRequiredService<KnowledgeDbContext>());
    }

    private async Task<TResult> InScopeAsync<TResult>(Func<IPeopleRepository, Task<TResult>> action)
    {
        await using var scope = Fixture.Services.CreateAsyncScope();
        return await action(Repository(scope.ServiceProvider));
    }

    private async Task InScopeAsync(Func<IPeopleRepository, Task> action)
    {
        await using var scope = Fixture.Services.CreateAsyncScope();
        await action(Repository(scope.ServiceProvider));
    }

    private static PeopleRepository Repository(IServiceProvider services) =>
        new(services.GetRequiredService<KnowledgeDbContext>(), services.GetRequiredService<DbContextGate>());
}

public sealed class InMemoryPeopleRepositoryTests(InMemoryPersistenceFixture fixture)
    : PeopleRepositoryContractTests<InMemoryPersistenceFixture>(fixture), IClassFixture<InMemoryPersistenceFixture>;

public sealed class PostgresPeopleRepositoryTests(PostgresPersistenceFixture fixture)
    : PeopleRepositoryContractTests<PostgresPersistenceFixture>(fixture), IClassFixture<PostgresPersistenceFixture>;
