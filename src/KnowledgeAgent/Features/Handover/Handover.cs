using KnowledgeAgent.Features.Chat;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KnowledgeAgent.Features.Handover;

public enum HandoverStatus
{
    Pending,

    Active,

    Resolved,

    Cancelled,

    Expired,
}

public enum HandoverSource
{
    Agent,
    Visitor,
    Staff,
}

public sealed class Handover
{
    public const int TokenLength = 12;

    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid ConversationId { get; set; }
    public Guid? WidgetId { get; set; }

    public string Channel { get; set; } = string.Empty;
    public HandoverStatus Status { get; set; }
    public HandoverSource Source { get; set; }
    public string? Reason { get; set; }
    public string? Summary { get; set; }

    public string Token { get; set; } = string.Empty;

    public string? Assignee { get; set; }
    public string? AssigneeId { get; set; }
    public string? SlackChannelId { get; set; }
    public string? SlackThreadTs { get; set; }
    public string? EmailMessageId { get; set; }
    public DateTimeOffset? EmailedAt { get; set; }
    public DateTimeOffset? EscalatedAt { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset? AcceptedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class HandoverInbound
{
    public string Id { get; set; } = string.Empty;
    public DateTimeOffset ReceivedAt { get; set; }
}

internal sealed class HandoverConfiguration : IEntityTypeConfiguration<Handover>
{
    public void Configure(EntityTypeBuilder<Handover> builder)
    {
        builder.ToTable("conversation_handovers");
        builder.HasKey(handover => handover.Id);
        builder.HasIndex(handover => handover.Token).IsUnique();
        builder.HasIndex(handover => new { handover.ConversationId, handover.Status });
        builder.HasIndex(handover => new { handover.SlackChannelId, handover.SlackThreadTs });
        builder.Property(handover => handover.Channel).HasMaxLength(16);
        builder.Property(handover => handover.Status).HasMaxLength(16);
        builder.Property(handover => handover.Source).HasMaxLength(16);
        builder.Property(handover => handover.Token).HasMaxLength(Handover.TokenLength);
        builder.Property(handover => handover.Reason).HasMaxLength(500);
        builder.Property(handover => handover.Summary).HasMaxLength(2000);
        builder.Property(handover => handover.Assignee).HasMaxLength(200);
        builder.Property(handover => handover.AssigneeId).HasMaxLength(200);
        builder.Property(handover => handover.SlackChannelId).HasMaxLength(32);
        builder.Property(handover => handover.SlackThreadTs).HasMaxLength(32);
        builder.Property(handover => handover.EmailMessageId).HasMaxLength(300);
        builder.Property(handover => handover.LastError).HasMaxLength(1000);
        builder.HasOne<Conversation>().WithMany().HasForeignKey(handover => handover.ConversationId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class HandoverInboundConfiguration : IEntityTypeConfiguration<HandoverInbound>
{
    public void Configure(EntityTypeBuilder<HandoverInbound> builder)
    {
        builder.ToTable("handover_inbound");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).HasMaxLength(400);
        builder.HasIndex(item => item.ReceivedAt);
    }
}
