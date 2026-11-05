using KnowledgeAgent.Features.Prompts;
using KnowledgeAgent.Features.Prompts.Langfuse;
using KnowledgeAgent.Features.Prompts.Rendering;
using KnowledgeAgent.Features.Prompts.Seeding;
using KnowledgeAgent.Infrastructure.Hosting;
using KnowledgeAgent.TestKit;
using KnowledgeAgent.Tests.Features.Prompts.Support;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace KnowledgeAgent.Tests.Features.Prompts;

public sealed class PromptsModuleTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Registers_the_prompt_services()
    {
        using var host = BuildHost(new Dictionary<string, string?>
        {
            ["Prompts:Store"] = "Local",
            ["Prompts:CacheTtlSeconds"] = "15",
        });
        var services = host.Services;

        var promptService = services.GetRequiredService<IPromptService>().ShouldBeOfType<PromptService>();
        promptService.UsesLangfuse.ShouldBeFalse();
        services.GetRequiredService<IPromptService>().ShouldBeSameAs(promptService);
        services.GetRequiredService<IPromptTemplateRenderer>().ShouldNotBeNull();
        services.GetRequiredService<LangfuseClient>().ShouldNotBeNull();
        services.GetRequiredService<IOptions<PromptOptions>>().Value.CacheTtlSeconds.ShouldBe(15);

        var initializer = services.GetServices<IStartupInitializer>().Single().ShouldBeOfType<PromptSeedingInitializer>();
        initializer.Order.ShouldBe(StartupOrder.Prompts);
        initializer.IsCritical.ShouldBeFalse();
        services.GetServices<IHostedService>().OfType<PromptSeedingService>().Count().ShouldBe(1);
        services.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations
            .Count(r => r.Name == "langfuse").ShouldBe(1);
    }

    [Fact]
    public void Adding_the_module_twice_registers_it_once()
    {
        var builder = CreateBuilder(new Dictionary<string, string?> { ["Prompts:Store"] = "Local" });
        builder.AddPrompts();
        builder.AddPrompts();
        using var host = builder.Build();

        host.Services.GetServices<IStartupInitializer>().Count().ShouldBe(1);
        host.Services.GetServices<IHostedService>().OfType<PromptSeedingService>().Count().ShouldBe(1);
        host.Services.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations.Count(r => r.Name == "langfuse").ShouldBe(1);
    }

    [Fact]
    public async Task Seeds_serves_and_reports_health_against_langfuse()
    {
        await using var server = await FakeLangfuseServer.StartAsync();
        using var host = BuildHost(LangfuseConfiguration(server));
        var services = host.Services;

        // Mirrors what the API host does before serving requests.
        foreach (var initializer in services.GetServices<IStartupInitializer>().OrderBy(i => i.Order))
        {
            await initializer.InitializeAsync(Ct);
        }

        PromptNames.All.ShouldAllBe(name => server.HasPrompt(name));

        var prompts = services.GetRequiredService<IPromptService>();
        var rendered = await prompts.RenderAsync(PromptNames.ConversationTitle, new Dictionary<string, object?> { ["message"] = "Hi" }, cancellationToken: Ct);
        rendered.Origin.ShouldBe(PromptOrigin.Langfuse);
        rendered.Text.ShouldEndWith("Message:\nHi");

        var list = await prompts.ListAsync(Ct);
        list.ShouldAllBe(summary => summary.Origin == PromptOrigin.Langfuse && summary.ExternalUrl != null);

        var health = services.GetRequiredService<HealthCheckService>();
        var healthy = await health.CheckHealthAsync(r => r.Name == "langfuse", Ct);
        healthy.Entries["langfuse"].Status.ShouldBe(HealthStatus.Healthy);

        server.Unavailable = true;
        var degraded = await health.CheckHealthAsync(r => r.Name == "langfuse", Ct);
        degraded.Entries["langfuse"].Status.ShouldBe(HealthStatus.Degraded);
        degraded.Entries["langfuse"].Description.ShouldNotBeNull().ShouldContain("unreachable");

        // Cached prompts keep working while Langfuse is down.
        (await prompts.GetTemplateAsync(PromptNames.ConversationTitle, cancellationToken: Ct)).Origin.ShouldBe(PromptOrigin.Langfuse);
    }

    [Fact]
    public async Task The_health_check_is_healthy_when_langfuse_is_not_configured()
    {
        using var host = BuildHost(new Dictionary<string, string?>());

        var report = await host.Services.GetRequiredService<HealthCheckService>().CheckHealthAsync(r => r.Name == "langfuse", Ct);

        report.Entries["langfuse"].Status.ShouldBe(HealthStatus.Healthy);
        report.Entries["langfuse"].Description.ShouldBe("not configured");
    }

    [Fact]
    public async Task Rejects_an_unknown_prompt_store_at_startup()
    {
        using var host = BuildHost(new Dictionary<string, string?> { ["Prompts:Store"] = "Redis" });

        var exception = await Should.ThrowAsync<OptionsValidationException>(() => host.StartAsync(Ct));

        exception.Message.ShouldContain("Prompts:Store");
    }

    [Fact]
    public async Task Starting_the_host_runs_background_seeding()
    {
        await using var server = await FakeLangfuseServer.StartAsync();
        using var host = BuildHost(LangfuseConfiguration(server));

        await host.StartAsync(Ct);
        try
        {
            (await Eventually.TrueAsync(() => PromptNames.All.All(server.HasPrompt), TimeSpan.FromSeconds(10), Ct)).ShouldBeTrue();
        }
        finally
        {
            await host.StopAsync(Ct);
        }
    }

    private static Dictionary<string, string?> LangfuseConfiguration(FakeLangfuseServer server) => new()
    {
        ["Prompts:Store"] = "Langfuse",
        ["Langfuse:BaseUrl"] = server.BaseAddress.ToString(),
        ["Langfuse:PublicKey"] = server.PublicKey,
        ["Langfuse:SecretKey"] = server.SecretKey,
    };

    private static IHost BuildHost(Dictionary<string, string?> configuration)
    {
        var builder = CreateBuilder(configuration);
        builder.AddPrompts();
        return builder.Build();
    }

    private static HostApplicationBuilder CreateBuilder(Dictionary<string, string?> configuration)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Configuration.AddInMemoryCollection(configuration);
        return builder;
    }
}
