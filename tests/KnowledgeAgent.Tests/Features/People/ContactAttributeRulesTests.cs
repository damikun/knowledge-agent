using System.Text;
using System.Text.Json;
using KnowledgeAgent.Features.People;
using KnowledgeAgent.Features.Widgets;

namespace KnowledgeAgent.Tests.Features.People;

public sealed class ContactAttributeRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_key_sent_overwrites_a_key_left_out_stays_and_null_clears()
    {
        var contact = new Contact();
        var registry = new Dictionary<string, ContactAttributeDefinition>(StringComparer.Ordinal);
        Merge(contact, registry, AttributeSources.Verified, Now, new { plan = "free", seats = 3, beta = true });

        var result = Merge(contact, registry, AttributeSources.Verified, Now.AddHours(1), new { plan = "pro", beta = (bool?)null });

        result.Applied.ShouldBe(["plan", "beta"], ignoreOrder: true);
        contact.Attributes["plan"].GetString().ShouldBe("pro");
        contact.Attributes["seats"].GetDecimal().ShouldBe(3, "a key left out keeps its last value");
        contact.Attributes.ShouldNotContainKey("beta", "null clears");
        contact.AttributeSources.ShouldNotContainKey("beta");
        contact.AttributeSources["plan"].UpdatedAt.ShouldBe(Now.AddHours(1));
        contact.AttributeSources["seats"].UpdatedAt.ShouldBe(Now);

        Merge(contact, registry, AttributeSources.Verified, Now.AddHours(2), new { plan = "pro" });
        contact.AttributeSources["plan"].UpdatedAt.ShouldBe(Now.AddHours(1));
    }

    [Fact]
    public void A_less_trusted_source_never_overwrites_or_clears_a_more_trusted_value()
    {
        var contact = new Contact();
        var registry = new Dictionary<string, ContactAttributeDefinition>(StringComparer.Ordinal);
        Merge(contact, registry, AttributeSources.Server, Now, new { plan = "enterprise" });
        Merge(contact, registry, AttributeSources.Browser, Now, new { company = "Acme" });

        var browser = Merge(contact, registry, AttributeSources.Browser, Now, new { plan = "free", company = "Other" });
        var verified = Merge(contact, registry, AttributeSources.Verified, Now, new { plan = (string?)null });

        browser.Rejected.ShouldHaveSingleItem().Key.ShouldBe("plan");
        browser.Rejected[0].Reason.ShouldContain("more trusted");
        verified.Rejected.ShouldHaveSingleItem().Key.ShouldBe("plan");
        contact.Attributes["plan"].GetString().ShouldBe("enterprise");
        contact.Attributes["company"].GetString().ShouldBe("Other", "browser may overwrite browser");

        // A more trusted source takes over a value and raises its source; admin and server overwrite each other.
        Merge(contact, registry, AttributeSources.Verified, Now, new { company = "Acme Inc" });
        contact.AttributeSources["company"].Source.ShouldBe(AttributeSources.Verified);
        Merge(contact, registry, AttributeSources.Admin, Now, new { plan = "trial" }).Rejected.ShouldBeEmpty();
        Merge(contact, registry, AttributeSources.Server, Now, new { plan = "pro" }).Rejected.ShouldBeEmpty();
        contact.Attributes["plan"].GetString().ShouldBe("pro");
    }

    [Fact]
    public void The_registry_trust_of_a_key_limits_who_may_set_it()
    {
        var contact = new Contact();
        var registry = new Dictionary<string, ContactAttributeDefinition>(StringComparer.Ordinal)
        {
            ["plan"] = new() { Key = "plan", Label = "Plan", Type = AttributeTypes.String, Trust = AttributeTrust.Server, LastSeenAt = Now },
            ["tier"] = new() { Key = "tier", Label = "Tier", Type = AttributeTypes.String, Trust = AttributeTrust.Verified, LastSeenAt = Now },
        };

        Merge(contact, registry, AttributeSources.Verified, Now, new { plan = "pro", tier = "gold" }).Rejected.Select(item => item.Key).ShouldBe(["plan"]);
        Merge(contact, registry, AttributeSources.Browser, Now, new { tier = "x" }).Rejected.Select(item => item.Key).ShouldBe(["tier"]);
        Merge(contact, registry, AttributeSources.Server, Now, new { plan = "pro" }).Applied.ShouldBe(["plan"]);
    }

    [Fact]
    public void Types_are_inferred_on_first_sight_and_later_values_convert_or_are_refused()
    {
        var contact = new Contact();
        var registry = new Dictionary<string, ContactAttributeDefinition>(StringComparer.Ordinal);
        var added = new List<ContactAttributeDefinition>();
        AttributeValues.Merge(contact, Values(new { plan = "pro", seats = 3, beta = false, renews = "2026-12-01", trial_ends = "2026-11-20T10:30:00+01:00" }), AttributeSources.Verified, Now, registry, added);

        added.ToDictionary(item => item.Key, item => item.Type).ShouldBe(
            new Dictionary<string, string>
            {
                ["plan"] = AttributeTypes.String,
                ["seats"] = AttributeTypes.Number,
                ["beta"] = AttributeTypes.Boolean,
                ["renews"] = AttributeTypes.Date,
                ["trial_ends"] = AttributeTypes.Date,
            },
            ignoreOrder: true);
        added.Single(item => item.Key == "trial_ends").Label.ShouldBe("Trial ends");
        contact.Attributes["renews"].GetString().ShouldBe("2026-12-01T00:00:00Z");
        contact.Attributes["trial_ends"].GetString().ShouldBe("2026-11-20T09:30:00Z", "dates are stored in UTC");

        var later = Merge(contact, registry, AttributeSources.Verified, Now, new { seats = "12", beta = "true", plan = 42, renews = "soon" });
        contact.Attributes["seats"].GetDecimal().ShouldBe(12);
        contact.Attributes["beta"].ValueKind.ShouldBe(JsonValueKind.True);
        contact.Attributes["plan"].GetString().ShouldBe("42", "a text attribute takes numbers as text");
        later.Rejected.ShouldHaveSingleItem().Key.ShouldBe("renews");
        Merge(contact, registry, AttributeSources.Verified, Now, new { seats = "many" }).Rejected.ShouldHaveSingleItem().Reason.ShouldBe("Expected a number.");
    }

    [Fact]
    public void Keys_values_and_counts_have_limits_with_clear_reasons()
    {
        var contact = new Contact();
        var registry = new Dictionary<string, ContactAttributeDefinition>(StringComparer.Ordinal);
        var refused = Merge(contact, registry, AttributeSources.Verified, Now, new Dictionary<string, object?>
        {
            ["Plan"] = "x",
            ["company.size"] = "x",
            ["9lives"] = "x",
            [new string('a', 41)] = "x",
            ["email"] = "x",
            ["nested"] = new { a = 1 },
            ["bio"] = new string('b', 256),
            ["ok"] = new string('c', 255),
        });

        refused.Applied.ShouldBe(["ok"]);
        refused.Rejected.Select(item => item.Key).ShouldBe(["Plan", "company.size", "9lives", new string('a', 41), "email", "nested", "bio"]);
        refused.Rejected.Single(item => item.Key == "email").Reason.ShouldContain("built-in");
        refused.Rejected.Single(item => item.Key == "bio").Reason.ShouldContain("255");

        var many = Enumerable.Range(0, 60).ToDictionary(index => $"key_{index}", object? (index) => index);
        var result = Merge(contact, registry, AttributeSources.Verified, Now, many);
        contact.Attributes.Count.ShouldBe(ContactAttributeLimits.MaxKeysPerContact);
        result.Rejected.Count.ShouldBe(60 + 1 - ContactAttributeLimits.MaxKeysPerContact);
        result.Rejected[^1].Reason.ShouldContain("at most 50");

        var full = Enumerable.Range(0, ContactAttributeLimits.MaxRegistryKeys).ToDictionary(index => $"r{index}", index => new ContactAttributeDefinition { Key = $"r{index}" });
        Merge(new Contact(), full, AttributeSources.Verified, Now, new { fresh = 1 }).Rejected.ShouldHaveSingleItem().Reason.ShouldContain("delete unused ones");
    }

    [Fact]
    public void Filters_come_as_base64url_json_or_as_repeated_f_parameters()
    {
        var conditions = new List<ContactCondition>
        {
            new("plan", "is", JsonSerializer.SerializeToElement("pro")),
            new("seats", "between", JsonSerializer.SerializeToElement(new[] { 5, 10 })),
        };
        var encoded = ContactFilters.Encode(conditions);
        encoded.ShouldNotContain("=");
        Convert.FromBase64String(encoded.Replace('-', '+').Replace('_', '/').PadRight(encoded.Length + ((4 - (encoded.Length % 4)) % 4), '='))
            .ShouldBe(Encoding.UTF8.GetBytes(ContactFilters.Serialize(conditions)));

        ContactFilters.TryParse(encoded, null, out var fromJson, out _).ShouldBeTrue();
        ContactFilters.TryParse(null, ["plan:is:pro", "seats:between:5..10", "last_seen:within_days:7", "beta:true", "url:contains:a:b"], out var fromF, out _).ShouldBeTrue();

        var registry = new Dictionary<string, ContactAttributeDefinition>(StringComparer.Ordinal)
        {
            ["plan"] = new() { Key = "plan", Type = AttributeTypes.String },
            ["seats"] = new() { Key = "seats", Type = AttributeTypes.Number },
            ["beta"] = new() { Key = "beta", Type = AttributeTypes.Boolean },
            ["url"] = new() { Key = "url", Type = AttributeTypes.String },
        };
        var errors = new List<string>();
        var resolved = ContactFilters.Resolve(fromF, registry, strict: true, errors);
        errors.ShouldBeEmpty();
        resolved[1].Number.ShouldBe(5);
        resolved[1].Number2.ShouldBe(10);
        resolved[2].ShouldBe(new ResolvedCondition("last_seen", true, AttributeTypes.Date, "within_days") { Days = 7 });
        resolved[4].Text.ShouldBe("a:b", "values may contain colons");
        ContactFilters.Resolve(fromJson, registry, strict: true, errors).Count.ShouldBe(2);

        ContactFilters.TryParse("not base64!", null, out _, out var error).ShouldBeFalse();
        error.ShouldNotBeNull();
        ContactFilters.Resolve([new("plan", "gt", JsonSerializer.SerializeToElement(1)), new("nope", "is", JsonSerializer.SerializeToElement("x"))], registry, strict: true, errors);
        errors.Count.ShouldBe(2);
        errors[0].ShouldContain("takes is, is_not, contains, set, not_set");
        ContactFilters.Resolve([new("gone", "gt", JsonSerializer.SerializeToElement(1))], registry, strict: false, []).ShouldHaveSingleItem().Type.ShouldBe(AttributeTypes.Number, "a saved segment keeps a deleted key");
    }

    [Fact]
    public void The_loader_page_is_stored_without_query_or_fragment()
    {
        var visit = VisitorService.VisitOf(new VisitorPageRequest("https://shop.example.com/cart?token=secret#pay", "https://google.com/search?q=me", "sk-SK"), newSession: true);
        visit.PageUrl.ShouldBe("https://shop.example.com/cart");
        visit.Referrer.ShouldBe("https://google.com/search");
        visit.Language.ShouldBe("sk-SK");
        VisitorService.VisitOf(new VisitorPageRequest("javascript:alert(1)", "file:///etc/passwd", "<script>"), newSession: false)
            .ShouldBe(new KnowledgeAgent.Features.People.VisitorVisit(false, null, null, null));
    }

    private static AttributeChangeResult Merge(Contact contact, Dictionary<string, ContactAttributeDefinition> registry, string source, DateTimeOffset now, object values) =>
        AttributeValues.Merge(contact, Values(values), source, now, registry, []);

    private static Dictionary<string, JsonElement> Values(object values) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(JsonSerializer.Serialize(values))!;
}
