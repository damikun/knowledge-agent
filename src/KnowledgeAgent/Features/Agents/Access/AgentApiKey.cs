using KnowledgeAgent.Features.Agents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KnowledgeAgent.Features.Agents.Access;

// Only a SHA-256 hash is stored; the key is shown once, on creation.
public sealed class AgentApiKey
{
    public const int MaxNameLength = 100;
    public const int DefaultRequestsPerMinute = 30;
    public const int MaxRequestsPerMinute = 600;

    public Guid Id { get; set; }

    public Guid AgentId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Prefix { get; set; } = string.Empty;

    public string Hash { get; set; } = string.Empty;

    public int RequestsPerMinute { get; set; } = DefaultRequestsPerMinute;

    public string? CreatedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? LastUsedAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }
}

internal sealed class AgentApiKeyConfiguration : IEntityTypeConfiguration<AgentApiKey>
{
    public void Configure(EntityTypeBuilder<AgentApiKey> builder)
    {
        builder.ToTable("agent_api_keys");
        builder.HasKey(key => key.Id);
        builder.HasIndex(key => key.Hash).IsUnique();
        builder.HasIndex(key => key.AgentId);
        builder.Property(key => key.Name).HasMaxLength(AgentApiKey.MaxNameLength);
        builder.Property(key => key.Prefix).HasMaxLength(16);
        builder.Property(key => key.Hash).HasMaxLength(64);
        builder.Property(key => key.CreatedBy).HasMaxLength(200);
        builder.HasOne<AgentConfig>()
            .WithMany()
            .HasForeignKey(key => key.AgentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
