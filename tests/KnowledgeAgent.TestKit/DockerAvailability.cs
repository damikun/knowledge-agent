using System.Diagnostics;

namespace KnowledgeAgent.TestKit;

public static class DockerAvailability
{
    private static readonly Lazy<bool> Available = new(Check);

    public const string SkipReason = "Docker is not available (or KA_SKIP_CONTAINER_TESTS=1).";

    public static bool IsAvailable => Available.Value;

    private static bool Check()
    {
        if (Environment.GetEnvironmentVariable("KA_SKIP_CONTAINER_TESTS") is "1" or "true")
        {
            return false;
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo("docker", "info --format {{.ServerVersion}}")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            });
            if (process is null)
            {
                return false;
            }

            if (!process.WaitForExit(TimeSpan.FromSeconds(15)))
            {
                process.Kill(entireProcessTree: true);
                return false;
            }

            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}

// Keep in sync with docker-compose.yml.
public static class ContainerImages
{
    public const string Postgres = "postgres:17-alpine";
    public const string ClickHouse = "clickhouse/clickhouse-server:25.12";
    public const string Inngest = "inngest/inngest:v1.49.0";
    public const string Keycloak = "quay.io/keycloak/keycloak:26.8.1";
    public const string GreenMail = "greenmail/standalone:2.1.14";
}
