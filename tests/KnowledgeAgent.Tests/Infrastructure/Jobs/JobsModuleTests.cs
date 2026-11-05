using KnowledgeAgent.Infrastructure.Jobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace KnowledgeAgent.Tests.Infrastructure.Jobs;

public sealed class JobsModuleTests
{
    [Fact]
    public void Jobs_run_in_process_by_default_and_are_registered_once()
    {
        using var host = BuildHost(builder => builder.AddJobs());
        var services = host.Services;

        var runner = services.GetServices<IHostedService>().OfType<InProcessJobRunner>().ShouldHaveSingleItem();
        services.GetServices<IHostedService>().OfType<InProcessScheduler>().ShouldHaveSingleItem();
        services.GetRequiredService<IJobQueue>().ShouldBeSameAs(runner);
        services.GetService<InngestJobEngine>().ShouldBeNull();
        services.GetServices<JobEngineInfo>().ShouldHaveSingleItem().ShouldBe(new JobEngineInfo("inprocess", DashboardUrl: null));
        services.GetServices<JobRegistration>().ShouldBeEmpty();
    }

    [Fact]
    public void The_first_handler_registered_for_a_kind_wins()
    {
        var services = new ServiceCollection();

        services.AddJob<FirstJob>("test.kind");
        services.AddJob<SecondJob>("test.kind");

        using var provider = services.BuildServiceProvider();
        provider.GetServices<JobRegistration>().ShouldHaveSingleItem().HandlerType.ShouldBe(typeof(FirstJob));
    }

    [Fact]
    public void Nonsensical_settings_fail_validation()
    {
        using var host = BuildHost(settings: new Dictionary<string, string?>
        {
            ["Jobs:Provider"] = "Database",
            ["Jobs:Inngest:DashboardUrl"] = "ftp://jobs.example.com",
            ["Jobs:Schedules:refresh-web-sources:Cron"] = "every night",
        });

        var exception = Should.Throw<OptionsValidationException>(() => host.Services.GetRequiredService<IOptions<JobsOptions>>().Value);

        exception.Failures.Count().ShouldBe(3);
        exception.Failures.ShouldContain(failure => failure.Contains("Jobs:Provider 'Database'"));
        exception.Failures.ShouldContain(failure => failure.Contains("Jobs:Inngest:DashboardUrl"));
        exception.Failures.ShouldContain(failure => failure.Contains("Jobs:Schedules:refresh-web-sources:Cron 'every night'"));
    }

    [Fact]
    public void Inngest_needs_an_app_id()
    {
        var options = new JobsOptions { Provider = "inngest", Inngest = new InngestJobsOptions { AppId = " " } };

        var result = new JobsOptionsValidator().Validate(name: null, options);

        result.Failures.ShouldNotBeNull().ShouldHaveSingleItem().ShouldContain("Jobs:Inngest:AppId");
    }

    private static IHost BuildHost(Action<HostApplicationBuilder>? configure = null, Dictionary<string, string?>? settings = null)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            DisableDefaults = true,
            EnvironmentName = Environments.Development,
        });
        builder.Configuration.AddInMemoryCollection(settings ?? []);
        builder.ConfigureContainer(new DefaultServiceProviderFactory(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }));
        builder.AddJobs();
        configure?.Invoke(builder);
        return builder.Build();
    }

    private sealed class FirstJob : IJobHandler
    {
        public Task HandleAsync(JobContext context, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class SecondJob : IJobHandler
    {
        public Task HandleAsync(JobContext context, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
