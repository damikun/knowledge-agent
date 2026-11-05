using System.Text.Json;
using System.Text.Json.Serialization;
using KnowledgeAgent.Infrastructure.Persistence.Conventions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KnowledgeAgent.Features.Connections.Mcp;

public enum McpAuthType
{
    None,

    Bearer,

    Header,
}

public sealed class McpServer
{
    public const int MaxKeyLength = 20;
    public const int MaxNameLength = 100;
    public const int MaxUrlLength = 2_000;
    public const int MaxHeaderNameLength = 100;
    public const int MaxSecretLength = 4_000;
    public const int MaxErrorLength = 500;

    public Guid Id { get; set; }

    // Part of the tool names the model sees (mcp_<key>__<tool>), so it never changes once created.
    public string Key { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;

    public McpAuthType AuthType { get; set; }

    public string? HeaderName { get; set; }

    // The bearer token or header value, encrypted with Data Protection; never returned by the API.
    public string? ProtectedSecret { get; set; }

    public string? SecretHint { get; set; }

    public bool Enabled { get; set; } = true;

    public DateTimeOffset? LastCheckedAt { get; set; }

    public bool? LastCheckOk { get; set; }

    public string? LastCheckError { get; set; }

    public string? ServerName { get; set; }

    public string? ServerVersion { get; set; }

    public List<McpToolInfo> Tools { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class McpToolInfo
{
    public string Name { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string? Description { get; set; }
    public JsonElement? InputSchema { get; set; }
    public bool? ReadOnlyHint { get; set; }
    public bool? DestructiveHint { get; set; }
    public bool? IdempotentHint { get; set; }
    public bool? OpenWorldHint { get; set; }

    // A tool that may change something unless the server says it only reads. The MCP default for destructiveHint is true.
    [JsonIgnore]
    public bool MayChangeThings => ReadOnlyHint != true;
}

internal sealed class McpServerConfiguration(bool isNpgsql) : IEntityTypeConfiguration<McpServer>
{
    public void Configure(EntityTypeBuilder<McpServer> builder)
    {
        builder.ToTable("mcp_servers");
        builder.HasKey(server => server.Id);
        builder.HasIndex(server => server.Key).IsUnique();
        builder.Property(server => server.Key).HasMaxLength(McpServer.MaxKeyLength);
        builder.Property(server => server.Name).HasMaxLength(McpServer.MaxNameLength);
        builder.Property(server => server.Url).HasMaxLength(McpServer.MaxUrlLength);
        builder.Property(server => server.AuthType).HasConversion<string>().HasMaxLength(16);
        builder.Property(server => server.HeaderName).HasMaxLength(McpServer.MaxHeaderNameLength);
        builder.Property(server => server.SecretHint).HasMaxLength(16);
        builder.Property(server => server.LastCheckError).HasMaxLength(McpServer.MaxErrorLength);
        builder.Property(server => server.ServerName).HasMaxLength(200);
        builder.Property(server => server.ServerVersion).HasMaxLength(100);

        var tools = builder.Property(server => server.Tools)
            .HasConversion(JsonColumns.Document<List<McpToolInfo>>(), JsonColumns.DocumentComparer<List<McpToolInfo>>())
            .IsRequired();
        if (isNpgsql)
        {
            tools.HasColumnType("jsonb");
            builder.Property(server => server.ProtectedSecret).HasColumnType("text");
        }
    }
}
