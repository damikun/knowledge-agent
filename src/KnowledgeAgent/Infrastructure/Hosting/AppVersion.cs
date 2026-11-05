using System.Reflection;

namespace KnowledgeAgent.Infrastructure.Hosting;

internal static class AppVersion
{
    // The assembly's version without the build metadata after '+', such as 1.4.0 for 1.4.0+3f2c1ab.
    public static string Of(Assembly assembly)
    {
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            var plus = informational.IndexOf('+', StringComparison.Ordinal);
            return plus > 0 ? informational[..plus] : informational;
        }

        return assembly.GetName().Version?.ToString() ?? "0.0.0";
    }
}
