using System.Diagnostics;
using KnowledgeAgent.Features.Prompts;
using KnowledgeAgent.Features.Prompts.Seeding;
using KnowledgeAgent.Infrastructure.Hosting;
using KnowledgeAgent.Infrastructure.Langfuse;
using KnowledgeAgent.TestKit;
using KnowledgeAgent.Tests.Features.Prompts.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace KnowledgeAgent.Tests.Features.Prompts.Seeding;

public sealed class PromptSeedingTests : IAsyncLifetime
{
    private static readonly PromptSeedingSettings FastSettings = new()
    {
        InitialAttemptTimeout = TimeSpan.FromSeconds(5),
        AttemptTimeout = TimeSpan.FromSeconds(5),
        RetryInterval = TimeSpan.FromMilliseconds(100),
        MaxDuration = TimeSpan.FromSeconds(30),
    };

    private readonly List<PromptHarness> _harnesses = [];
    private FakeLangfuseServer _server = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _server = await FakeLangfuseServer.StartAsync();

    public async ValueTask DisposeAsync()
    {
        foreach (var harness in _harnesses)
        {
            harness.Dispose();
        }

        await _server.DisposeAsync();
    }

    [Fact]
    public async Task Creates_every_missing_default_prompt_in_langfuse()
    {
        var harness = Harness();
        using var seeder = harness.CreateSeeder();

        var result = await seeder.SeedAsync(Ct);

        result.Created.ShouldBe(PromptNames.All, ignoreOrder: true);
        result.Existing.ShouldBeEmpty();
        seeder.IsCompleted.ShouldBeTrue();
        foreach (var name in PromptNames.All)
        {
            harness.Local.TryGet(name, out var local).ShouldBeTrue();
            var stored = await harness.Client.GetPromptAsync(name, cancellationToken: Ct);
            stored.ShouldNotBeNull();
            stored.Version.ShouldBe(1);
            stored.Text.ShouldBe(local!.Text);
            stored.Type.ShouldBe(PromptType.Text);
            stored.Labels.ShouldContain("production");
            stored.Tags.ShouldBe(["knowledge-agent"]);
            stored.Config.ShouldNotBeNull().Count.ShouldBe(0);
        }
    }

    [Fact]
    public async Task Never_touches_prompts_someone_created_or_edited()
    {
        _server.SetTextPrompt(PromptNames.OrchestratorSystem, "Edited in Langfuse");
        _server.SetTextPrompt(PromptNames.ResearcherSystem, "Draft", null, "staging");
        var harness = Harness();
        using var seeder = harness.CreateSeeder();

        var result = await seeder.SeedAsync(Ct);

        result.Created.ShouldBe(PromptNames.All.Except([PromptNames.OrchestratorSystem, PromptNames.ResearcherSystem]), ignoreOrder: true);
        result.Existing.ShouldBe([PromptNames.OrchestratorSystem, PromptNames.ResearcherSystem], ignoreOrder: true);
        _server.Versions(PromptNames.OrchestratorSystem).ShouldBe([1]);
        _server.Versions(PromptNames.ResearcherSystem).ShouldBe([1]);
        (await harness.Client.GetPromptAsync(PromptNames.OrchestratorSystem, cancellationToken: Ct))!.Text.ShouldBe("Edited in Langfuse");
        (await harness.Client.GetPromptAsync(PromptNames.ResearcherSystem, cancellationToken: Ct)).ShouldBeNull(); // still no production version
    }

    [Fact]
    public async Task Seeded_prompts_nobody_changed_get_the_new_default()
    {
        _server.SetCommittedTextPrompt(PromptNames.OrchestratorSystem, "The default of an older release", PromptSeeder.CommitMessage);
        var harness = Harness();
        harness.Local.TryGet(PromptNames.OrchestratorSystem, out var local).ShouldBeTrue();
        harness.Local.TryGet(PromptNames.ResearcherSystem, out var researcher).ShouldBeTrue();
        _server.SetCommittedTextPrompt(PromptNames.ResearcherSystem, researcher!.Text!, PromptSeeder.CommitMessage);
        using var seeder = harness.CreateSeeder();

        var result = await seeder.SeedAsync(Ct);

        result.Updated.ShouldBe([PromptNames.OrchestratorSystem]);
        result.Existing.ShouldBe([PromptNames.ResearcherSystem], "it already holds the current default");
        _server.Versions(PromptNames.OrchestratorSystem).ShouldBe([1, 2]);
        var production = (await harness.Client.GetPromptAsync(PromptNames.OrchestratorSystem, cancellationToken: Ct)).ShouldNotBeNull();
        production.Version.ShouldBe(2);
        production.Text.ShouldBe(local!.Text);
        production.CommitMessage.ShouldBe(PromptSeeder.CommitMessage);

        using var again = Harness().CreateSeeder();
        (await again.SeedAsync(Ct)).Updated.ShouldBeEmpty();
        _server.Versions(PromptNames.OrchestratorSystem).ShouldBe([1, 2]);
    }

    [Fact]
    public async Task Seeded_prompts_that_were_edited_or_rolled_back_keep_their_owners_choice()
    {
        _server.SetCommittedTextPrompt(PromptNames.OrchestratorSystem, "Seeded", PromptSeeder.CommitMessage);
        _server.SetCommittedTextPrompt(PromptNames.OrchestratorSystem, "Our own tone", "Tweaked the tone");
        _server.SetCommittedTextPrompt(PromptNames.ResearcherSystem, "Seeded", PromptSeeder.CommitMessage);
        _server.SetCommittedTextPrompt(PromptNames.ResearcherSystem, "Shorter reports", null);

        // Production points at the seeded version, but a newer one exists: someone is trying something out.
        _server.SetCommittedTextPrompt(PromptNames.ConversationTitle, "Seeded", PromptSeeder.CommitMessage);
        _server.SetTextPrompt(PromptNames.ConversationTitle, "Experiment", null, "staging");
        var harness = Harness();
        using var seeder = harness.CreateSeeder();

        var result = await seeder.SeedAsync(Ct);

        result.Updated.ShouldBeEmpty();
        result.Existing.ShouldBe([PromptNames.OrchestratorSystem, PromptNames.ResearcherSystem, PromptNames.ConversationTitle], ignoreOrder: true);
        _server.Versions(PromptNames.OrchestratorSystem).ShouldBe([1, 2]);
        _server.Versions(PromptNames.ResearcherSystem).ShouldBe([1, 2]);
        _server.Versions(PromptNames.ConversationTitle).ShouldBe([1, 2]);
        (await harness.Client.GetPromptAsync(PromptNames.OrchestratorSystem, cancellationToken: Ct))!.Text.ShouldBe("Our own tone");
        (await harness.Client.GetPromptAsync(PromptNames.ConversationTitle, cancellationToken: Ct))!.Text.ShouldBe("Seeded");
    }

    [Fact]
    public async Task Does_nothing_after_a_successful_attempt()
    {
        var harness = Harness();
        using var seeder = harness.CreateSeeder();
        await seeder.SeedAsync(Ct);
        var requests = _server.RequestLog.Count;

        var again = await seeder.SeedAsync(Ct);

        again.Created.ShouldBeEmpty();
        _server.RequestLog.Count.ShouldBe(requests);
    }

    [Theory]
    [InlineData("seed-off")]
    [InlineData("local-store")]
    [InlineData("not-configured")]
    public async Task Is_skipped_when_disabled(string scenario)
    {
        var harness = scenario switch
        {
            "seed-off" => Harness(prompts => prompts.SeedOnStartup = false),
            "local-store" => Harness(prompts => prompts.Store = "Local"),
            _ => Harness(langfuse: options => options.BaseUrl = null),
        };
        using var seeder = harness.CreateSeeder();
        var initializer = new PromptSeedingInitializer(seeder, FastSettings, TimeProvider.System, NullLogger<PromptSeedingInitializer>.Instance);
        using var service = new PromptSeedingService(seeder, FastSettings, TimeProvider.System, NullLogger<PromptSeedingService>.Instance);

        await initializer.InitializeAsync(Ct);
        await service.StartAsync(Ct);
        await service.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5), Ct);

        seeder.IsEnabled.ShouldBeFalse();
        seeder.DisabledReason.ShouldNotBeNullOrWhiteSpace();
        (await seeder.SeedAsync(Ct)).Created.ShouldBeEmpty();
        _server.RequestLog.ShouldBeEmpty();
    }

    [Fact]
    public void The_initializer_runs_after_persistence_and_is_not_critical()
    {
        var harness = Harness();
        using var seeder = harness.CreateSeeder();
        IStartupInitializer initializer = new PromptSeedingInitializer(seeder, FastSettings, TimeProvider.System, NullLogger<PromptSeedingInitializer>.Instance);

        initializer.Order.ShouldBe(StartupOrder.Prompts);
        initializer.IsCritical.ShouldBeFalse();
        initializer.Name.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task The_initializer_seeds_when_langfuse_is_up()
    {
        var harness = Harness();
        using var seeder = harness.CreateSeeder();
        var initializer = new PromptSeedingInitializer(seeder, FastSettings, TimeProvider.System, NullLogger<PromptSeedingInitializer>.Instance);

        await initializer.InitializeAsync(Ct);

        seeder.IsCompleted.ShouldBeTrue();
        PromptNames.All.ShouldAllBe(name => _server.HasPrompt(name));
    }

    [Fact]
    public async Task The_initializer_swallows_failures_when_langfuse_is_down()
    {
        _server.Unavailable = true;
        var harness = Harness();
        using var seeder = harness.CreateSeeder();
        var initializer = new PromptSeedingInitializer(seeder, FastSettings, TimeProvider.System, NullLogger<PromptSeedingInitializer>.Instance);

        await initializer.InitializeAsync(Ct);

        seeder.IsCompleted.ShouldBeFalse();
        seeder.LastAttemptAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task The_initializer_does_not_block_startup_when_langfuse_hangs()
    {
        await using var hanging = new HangingTcpServer();
        var harness = Harness(langfuse: options => options.BaseUrl = hanging.BaseAddress.ToString());
        using var seeder = harness.CreateSeeder();
        var settings = new PromptSeedingSettings { InitialAttemptTimeout = TimeSpan.FromMilliseconds(300) };
        var initializer = new PromptSeedingInitializer(seeder, settings, TimeProvider.System, NullLogger<PromptSeedingInitializer>.Instance);
        var stopwatch = Stopwatch.StartNew();

        await initializer.InitializeAsync(Ct);

        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(3));
        seeder.IsCompleted.ShouldBeFalse();
    }

    [Fact]
    public async Task The_background_service_retries_until_langfuse_becomes_available()
    {
        _server.Unavailable = true;
        var harness = Harness();
        using var seeder = harness.CreateSeeder();
        var initializer = new PromptSeedingInitializer(seeder, FastSettings, TimeProvider.System, NullLogger<PromptSeedingInitializer>.Instance);
        using var service = new PromptSeedingService(seeder, FastSettings, TimeProvider.System, NullLogger<PromptSeedingService>.Instance);

        await initializer.InitializeAsync(Ct);
        await service.StartAsync(Ct);
        (await Eventually.TrueAsync(() => ListRequests() >= 3, TimeSpan.FromSeconds(10), Ct)).ShouldBeTrue("the service should keep retrying");
        seeder.IsCompleted.ShouldBeFalse();

        _server.Unavailable = false;
        await service.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(10), Ct);

        seeder.IsCompleted.ShouldBeTrue();
        PromptNames.All.ShouldAllBe(name => _server.HasPrompt(name));
        await service.StopAsync(Ct);
    }

    [Fact]
    public async Task The_background_service_gives_up_after_the_maximum_duration()
    {
        _server.Unavailable = true;
        var harness = Harness();
        using var seeder = harness.CreateSeeder();
        var settings = new PromptSeedingSettings { RetryInterval = TimeSpan.FromMilliseconds(50), MaxDuration = TimeSpan.FromMilliseconds(300) };
        using var service = new PromptSeedingService(seeder, settings, TimeProvider.System, NullLogger<PromptSeedingService>.Instance);

        await service.StartAsync(Ct);
        await service.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(10), Ct);

        seeder.IsCompleted.ShouldBeFalse();
        ListRequests().ShouldBeGreaterThanOrEqualTo(2);
        PromptNames.All.ShouldAllBe(name => !_server.HasPrompt(name));
    }

    [Fact]
    public async Task The_background_service_exits_immediately_when_the_initializer_succeeded()
    {
        var harness = Harness();
        using var seeder = harness.CreateSeeder();
        await new PromptSeedingInitializer(seeder, FastSettings, TimeProvider.System, NullLogger<PromptSeedingInitializer>.Instance).InitializeAsync(Ct);
        var requests = _server.RequestLog.Count;
        using var service = new PromptSeedingService(seeder, FastSettings, TimeProvider.System, NullLogger<PromptSeedingService>.Instance);

        await service.StartAsync(Ct);
        await service.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5), Ct);

        _server.RequestLog.Count.ShouldBe(requests);
    }

    private PromptHarness Harness(Action<PromptOptions>? prompts = null, Action<LangfuseOptions>? langfuse = null)
    {
        var harness = new PromptHarness(_server, prompts, langfuse);
        _harnesses.Add(harness);
        return harness;
    }

    private int ListRequests() => _server.RequestLog.Count(r => r.StartsWith("GET /api/public/v2/prompts?", StringComparison.Ordinal));
}
