using KnowledgeAgent.Infrastructure.VectorStore;

namespace KnowledgeAgent.Tests.Infrastructure.VectorStore;

public sealed class ChunkMetadataSerializerTests
{
    [Fact]
    public void RoundTrips_UnicodeAndSpecialCharacters()
    {
        var metadata = new Dictionary<string, string>
        {
            ["title"] = "Žluťoučký kůň “quoted” 🚀",
            ["url"] = "https://example.com/?q=a&b=<c>",
            ["page"] = "3",
            ["multi line"] = "line 1\nline 2\t\"tab\" \\ backslash",
            ["empty"] = string.Empty,
        };

        var json = ChunkMetadataSerializer.Serialize(metadata);
        var restored = ChunkMetadataSerializer.Deserialize(json);

        restored.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ShouldBe(metadata.OrderBy(pair => pair.Key, StringComparer.Ordinal));
        json.ShouldContain("Žluťoučký kůň");
    }

    [Fact]
    public void Serialize_EmptyOrNull_GivesAnEmptyObject()
    {
        ChunkMetadataSerializer.Serialize(null).ShouldBe("{}");
        ChunkMetadataSerializer.Serialize(new Dictionary<string, string>()).ShouldBe("{}");
    }

    [Fact]
    public void Serialize_DropsNullValues()
    {
        var metadata = new Dictionary<string, string> { ["title"] = "Guide", ["url"] = null! };

        ChunkMetadataSerializer.Serialize(metadata).ShouldBe("""{"title":"Guide"}""");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    [InlineData("[1, 2]")]
    [InlineData("\"text\"")]
    [InlineData("null")]
    [InlineData("{\"unterminated\": ")]
    public void Deserialize_AnythingButAJsonObject_GivesAnEmptyDictionary(string? json)
    {
        var metadata = ChunkMetadataSerializer.Deserialize(json);

        metadata.ShouldNotBeNull();
        metadata.ShouldBeEmpty();
    }

    [Fact]
    public void Deserialize_ReturnsNonStringValuesAsRawJson_AndSkipsNulls()
    {
        var metadata = ChunkMetadataSerializer.Deserialize("""{"page": 3, "flag": true, "nested": {"a": 1}, "list": [1, "x"], "nothing": null}""");

        metadata["page"].ShouldBe("3");
        metadata["flag"].ShouldBe("true");
        metadata["nested"].ShouldBe("""{"a": 1}""");
        metadata["list"].ShouldBe("""[1, "x"]""");
        metadata.ContainsKey("nothing").ShouldBeFalse();
    }

    [Fact]
    public void Copy_IsReadOnly_AndIndependentOfTheSource()
    {
        var source = new Dictionary<string, string> { ["title"] = "original", ["dropped"] = null! };

        var copy = ChunkMetadataSerializer.Copy(source);
        source["title"] = "changed";

        copy.ShouldHaveSingleItem().ShouldBe(new KeyValuePair<string, string>("title", "original"));
        Should.Throw<NotSupportedException>(() => ((IDictionary<string, string>)copy).Add("x", "y"));
    }
}
