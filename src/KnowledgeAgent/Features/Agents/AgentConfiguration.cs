using KnowledgeAgent.Infrastructure.Persistence.Conventions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KnowledgeAgent.Features.Agents;

internal sealed class AgentConfiguration(bool isNpgsql) : IEntityTypeConfiguration<AgentConfig>
{
    public void Configure(EntityTypeBuilder<AgentConfig> builder)
    {
        builder.ToTable("agents");
        builder.HasKey(agent => agent.Id);
        builder.HasIndex(agent => agent.Key).IsUnique();
        builder.Property(agent => agent.Key).HasMaxLength(AgentConfig.MaxKeyLength);
        builder.Property(agent => agent.Name).HasMaxLength(AgentConfig.MaxNameLength);
        builder.Property(agent => agent.Description).HasMaxLength(AgentConfig.MaxDescriptionLength);
        builder.Property(agent => agent.Instructions).HasMaxLength(AgentConfig.MaxInstructionsLength);
        builder.Property(agent => agent.Language).HasMaxLength(AgentConfig.MaxLanguageLength);
        builder.Property(agent => agent.ProfileName).HasMaxLength(100);
        builder.Property(agent => agent.HandoverPolicy).HasConversion<string>().HasMaxLength(16);

        var folders = builder.Property(agent => agent.FolderIds)
            .HasConversion(JsonColumns.GuidList, JsonColumns.GuidListComparer)
            .IsRequired();
        var tools = builder.Property(agent => agent.Tools)
            .HasConversion(JsonColumns.StringList, JsonColumns.StringListComparer)
            .IsRequired();
        var mcpTools = builder.Property(agent => agent.McpTools)
            .HasConversion(JsonColumns.Document<List<AgentMcpApp>>(), JsonColumns.DocumentComparer<List<AgentMcpApp>>())
            .IsRequired();
        var specialists = builder.Property(agent => agent.Specialists)
            .HasConversion(JsonColumns.Document<List<AgentSpecialist>>(), JsonColumns.DocumentComparer<List<AgentSpecialist>>())
            .IsRequired();
        if (isNpgsql)
        {
            specialists.HasColumnType("jsonb");
            folders.HasColumnType("jsonb");
            tools.HasColumnType("jsonb");
            mcpTools.HasColumnType("jsonb");
        }
    }
}
