using KnowledgeAgent.Features.Prompts;

namespace KnowledgeAgent.Tests.Features.Prompts;

public sealed class LruCacheTests
{
    [Fact]
    public void Evicts_the_least_recently_used_entry()
    {
        var cache = new LruCache<string, int>(2);
        cache.Set("a", 1);
        cache.Set("b", 2);
        cache.TryGet("a", out _).ShouldBeTrue(); // "b" is now the oldest

        cache.Set("c", 3);

        cache.Count.ShouldBe(2);
        cache.TryGet("b", out _).ShouldBeFalse();
        cache.TryGet("a", out var a).ShouldBeTrue();
        a.ShouldBe(1);
        cache.TryGet("c", out var c).ShouldBeTrue();
        c.ShouldBe(3);
    }

    [Fact]
    public void Updating_an_entry_replaces_the_value_without_growing()
    {
        var cache = new LruCache<string, int>(2);
        cache.Set("a", 1);
        cache.Set("a", 5);

        cache.Count.ShouldBe(1);
        cache.TryGet("a", out var value).ShouldBeTrue();
        value.ShouldBe(5);

        cache.Clear();
        cache.Count.ShouldBe(0);
    }

    [Fact]
    public void Requires_a_positive_capacity() => Should.Throw<ArgumentOutOfRangeException>(() => new LruCache<string, int>(0));

    [Fact]
    public void Is_safe_under_concurrent_use()
    {
        var cache = new LruCache<int, int>(16);

        Parallel.For(0, 10_000, i =>
        {
            cache.Set(i % 64, i);
            cache.TryGet((i + 7) % 64, out _);
        });

        cache.Count.ShouldBe(16);
    }
}
