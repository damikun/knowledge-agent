using ClickHouse.Driver.ADO;

namespace KnowledgeAgent.Infrastructure.VectorStore.ClickHouse;

internal static class ClickHouseConnectionSettings
{
    private const string EndpointKey = "Endpoint";

    // Error messages never include the connection string because it carries the password.
    public static ClickHouseClientSettings Create(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("The ClickHouse connection string is empty.");
        }

        ClickHouseConnectionStringBuilder builder;
        try
        {
            builder = new ClickHouseConnectionStringBuilder(connectionString);
        }
        catch (ArgumentException ex)
        {
            throw new InvalidOperationException($"The ClickHouse connection string is malformed: {ex.Message}", ex);
        }

        // Aspire-style connection strings carry Endpoint=http://host:port instead of Host and Port.
        if (builder.TryGetValue(EndpointKey, out var endpointValue))
        {
            ApplyEndpoint(builder, Convert.ToString(endpointValue, System.Globalization.CultureInfo.InvariantCulture));
        }

        try
        {
            return new ClickHouseClientSettings(builder)
            {
                // The store creates its database itself, so the connection must not point at one that may not exist yet.
                Database = string.Empty,

                // A ClickHouse session rejects concurrent queries, and the store runs queries concurrently.
                UseSession = false,
            };
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or InvalidCastException or OverflowException)
        {
            throw new InvalidOperationException($"The ClickHouse connection string is invalid: {ex.Message}", ex);
        }
    }

    private static void ApplyEndpoint(ClickHouseConnectionStringBuilder builder, string? endpoint)
    {
        builder.Remove(EndpointKey);
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return;
        }

        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("The ClickHouse connection string Endpoint must be an absolute http(s) URL.");
        }

        builder.Protocol = uri.Scheme;
        builder.Host = uri.Host;
        builder.Port = checked((ushort)uri.Port);
        if (uri.AbsolutePath.Length > 1)
        {
            builder.Path = uri.AbsolutePath;
        }
    }
}
