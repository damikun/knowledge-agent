using KnowledgeAgent.Infrastructure.AI;
using KnowledgeAgent.TestKit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KnowledgeAgent.Api.Tests.Support;

public sealed class KnowledgeAgentFactory(
    IReadOnlyDictionary<string, string?> settings,
    FakeAiProvider aiProvider,
    TestLogSink logSink,
    Action<IServiceCollection>? configureServices = null) : WebApplicationFactory<Program>
{
    public const string EnvironmentName = "Testing";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(EnvironmentName);

        foreach (var (key, value) in settings)
        {
            builder.UseSetting(key, value ?? string.Empty);
        }

        builder.ConfigureLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddProvider(new TestLoggerProvider(logSink));
            logging.SetMinimumLevel(LogLevel.Information);
            logging.AddFilter("Microsoft", LogLevel.Warning);
            logging.AddFilter("System", LogLevel.Warning);
            logging.AddFilter("Polly", LogLevel.Warning);
            logging.AddFilter("KnowledgeAgent", LogLevel.Debug);
        });

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IAiProvider>(aiProvider);
            configureServices?.Invoke(services);
        });
    }
}
