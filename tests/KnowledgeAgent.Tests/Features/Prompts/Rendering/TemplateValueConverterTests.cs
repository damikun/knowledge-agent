using System.Collections;
using System.Text.Json;
using System.Text.Json.Nodes;
using KnowledgeAgent.Features.Prompts;
using KnowledgeAgent.Features.Prompts.Rendering;

namespace KnowledgeAgent.Tests.Features.Prompts.Rendering;

public sealed class TemplateValueConverterTests
{
    [Fact]
    public void Converts_json_elements_to_plain_clr_values()
    {
        using var document = JsonDocument.Parse("""{"s":"x","i":42,"big":12345678901234,"d":1.5,"t":true,"f":false,"n":null,"a":[1,"two"],"o":{"k":"v"}}""");

        var result = TemplateValueConverter.Convert(document.RootElement).ShouldBeOfType<Dictionary<string, object?>>();

        result["s"].ShouldBe("x");
        result["i"].ShouldBe(42L);
        result["big"].ShouldBe(12345678901234L);
        result["d"].ShouldBe(1.5);
        result["t"].ShouldBe(true);
        result["f"].ShouldBe(false);
        result["n"].ShouldBeNull();
        result["a"].ShouldBeOfType<List<object?>>().ShouldBe(new List<object?> { 1L, "two" });
        result["o"].ShouldBeOfType<Dictionary<string, object?>>()["K"].ShouldBe("v"); // keys are case-insensitive
    }

    [Fact]
    public void Converts_json_nodes_including_values_created_from_clr_objects()
    {
        var node = new JsonObject
        {
            ["parsed"] = JsonNode.Parse("""{"x":[1,2]}"""),
            ["number"] = JsonValue.Create(7),
            ["double"] = JsonValue.Create(2.5),
            ["guid"] = JsonValue.Create(Guid.Empty),
            ["text"] = "hello",
            ["nothing"] = null,
        };

        var result = TemplateValueConverter.Convert(node).ShouldBeOfType<Dictionary<string, object?>>();

        result["parsed"].ShouldBeOfType<Dictionary<string, object?>>()["x"].ShouldBe(new List<object?> { 1L, 2L });
        result["number"].ShouldBe(7L);
        result["double"].ShouldBe(2.5);
        result["guid"].ShouldBe(Guid.Empty.ToString());
        result["text"].ShouldBe("hello");
        result["nothing"].ShouldBeNull();
    }

    [Fact]
    public void Converts_objects_dictionaries_and_sequences_recursively()
    {
        using var tags = JsonDocument.Parse("""["a","b"]""");
        var value = new
        {
            Name = "Ann",
            Tags = tags.RootElement.Clone(),
            Scores = new Dictionary<string, int> { ["math"] = 1 },
            Legacy = new Hashtable { ["key"] = "value" },
            Custom = new ReadOnlyLookup(new Dictionary<string, string> { ["k"] = "v" }),
            Pairs = new[] { new KeyValuePair<string, int>("p", 1) },
        };

        var result = TemplateValueConverter.Convert(value).ShouldBeOfType<Dictionary<string, object?>>();

        result["name"].ShouldBe("Ann");
        result["tags"].ShouldBe(new List<object?> { "a", "b" });
        result["scores"].ShouldBeOfType<Dictionary<string, object?>>()["math"].ShouldBe(1);
        result["legacy"].ShouldBeOfType<Dictionary<string, object?>>()["key"].ShouldBe("value");
        result["custom"].ShouldBeOfType<Dictionary<string, object?>>()["k"].ShouldBe("v");
        result["pairs"].ShouldBeOfType<Dictionary<string, object?>>()["p"].ShouldBe(1); // sequences of string-keyed pairs act as dictionaries
    }

    [Fact]
    public void Keeps_scalars_and_framework_types_as_they_are()
    {
        var moment = DateTimeOffset.UnixEpoch;
        var uri = new Uri("https://example.com");

        TemplateValueConverter.Convert(moment).ShouldBe(moment);
        TemplateValueConverter.Convert(uri).ShouldBeSameAs(uri);
        TemplateValueConverter.Convert(DayOfWeek.Friday).ShouldBe(DayOfWeek.Friday);
        TemplateValueConverter.Convert(typeof(string)).ShouldBe(typeof(string));
    }

    [Fact]
    public void Rejects_absurdly_deep_graphs()
    {
        object value = "leaf";
        for (var i = 0; i < TemplateValueConverter.MaxDepth + 1; i++)
        {
            value = new List<object> { value };
        }

        Should.Throw<PromptRenderException>(() => TemplateValueConverter.Convert(value));
    }

    private sealed class ReadOnlyLookup(IReadOnlyDictionary<string, string> inner) : IReadOnlyDictionary<string, string>
    {
        public int Count => inner.Count;

        public IEnumerable<string> Keys => inner.Keys;

        public IEnumerable<string> Values => inner.Values;

        public string this[string key] => inner[key];

        public bool ContainsKey(string key) => inner.ContainsKey(key);

        public bool TryGetValue(string key, out string value) => inner.TryGetValue(key, out value!);

        public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => inner.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
