using KnowledgeAgent.Features.Chat;
using KnowledgeAgent.Features.Staff;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KnowledgeAgent.Features.Conversations;

public sealed class ConversationRead
{
    public string UserId { get; set; } = string.Empty;
    public Guid ConversationId { get; set; }
    public int LastReadSequence { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

internal sealed class ConversationReadConfiguration : IEntityTypeConfiguration<ConversationRead>
{
    public void Configure(EntityTypeBuilder<ConversationRead> builder)
    {
        builder.ToTable("conversation_reads");
        builder.HasKey(read => new { read.UserId, read.ConversationId });
        builder.Property(read => read.UserId).HasMaxLength(StaffMember.MaxIdLength);

        builder.HasOne<Conversation>().WithMany().HasForeignKey(read => read.ConversationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(read => read.ConversationId);
    }
}
