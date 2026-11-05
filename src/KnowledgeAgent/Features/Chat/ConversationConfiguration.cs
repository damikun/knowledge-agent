using KnowledgeAgent.Features.People;
using KnowledgeAgent.Infrastructure.Persistence.Conventions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KnowledgeAgent.Features.Chat;

internal sealed class ConversationConfiguration(bool isNpgsql) : IEntityTypeConfiguration<Conversation>
{
    public void Configure(EntityTypeBuilder<Conversation> builder)
    {
        builder.ToTable("conversations");
        builder.HasKey(conversation => conversation.Id);

        var sourceIds = builder.Property(conversation => conversation.SourceIds)
            .HasConversion(JsonColumns.GuidList, JsonColumns.GuidListComparer)
            .IsRequired();
        if (isNpgsql)
        {
            sourceIds.HasColumnType("jsonb");
        }

        builder.HasMany(conversation => conversation.Turns)
            .WithOne()
            .HasForeignKey(turn => turn.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(conversation => conversation.Messages)
            .WithOne()
            .HasForeignKey(message => message.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Property(conversation => conversation.Channel).HasMaxLength(16);
        builder.Property(conversation => conversation.Status).HasMaxLength(24);

        builder.HasIndex(conversation => conversation.UpdatedAt);
        builder.HasIndex(conversation => new { conversation.WidgetId, conversation.UpdatedAt });
        builder.HasIndex(conversation => conversation.VisitorId);
        builder.Property(conversation => conversation.CreatedById).HasMaxLength(200);
        builder.Property(conversation => conversation.ActiveAgentName).HasMaxLength(100);
        builder.Property(conversation => conversation.CreatedByName).HasMaxLength(200);

        // Erasing the person deletes these explicitly, so the link only clears.
        builder.HasOne<Contact>().WithMany().HasForeignKey(conversation => conversation.ContactId).OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(conversation => new { conversation.ContactId, conversation.UpdatedAt });
        builder.HasIndex(conversation => conversation.CreatedById);

        builder.Property(conversation => conversation.AssigneeId).HasMaxLength(200);
        builder.Property(conversation => conversation.AssigneeName).HasMaxLength(200);
        builder.HasIndex(conversation => new { conversation.AssigneeId, conversation.Status });

        builder.HasIndex(conversation => new { conversation.Pinned, conversation.UpdatedAt, conversation.Id });

        builder.HasIndex(conversation => conversation.CreatedAt);
        if (isNpgsql)
        {
            builder.HasIndex(conversation => conversation.Title).HasMethod("gin").HasOperators("gin_trgm_ops");
        }
    }
}

internal sealed class ConversationTurnConfiguration(bool isNpgsql) : IEntityTypeConfiguration<ConversationTurn>
{
    public const string SequenceIndexName = "ix_conversation_turns_conversation_id_sequence";

    public void Configure(EntityTypeBuilder<ConversationTurn> builder)
    {
        builder.ToTable("conversation_turns");
        builder.HasKey(turn => turn.Id);
        builder.HasIndex(turn => new { turn.ConversationId, turn.Sequence }).IsUnique();
        builder.Property(turn => turn.TraceId).HasMaxLength(32);

        builder.HasIndex(turn => turn.CreatedAt);

        var toolCalls = builder.Property(turn => turn.ToolCallsJson).HasConversion(JsonColumns.JsonText);
        var citations = builder.Property(turn => turn.CitationsJson).HasConversion(JsonColumns.JsonText);

        if (isNpgsql)
        {
            toolCalls.HasColumnType("jsonb");
            citations.HasColumnType("jsonb");

            // Stored as text, not jsonb: the messages are replayed to the model and must round-trip byte for byte.
            builder.Property(turn => turn.ModelMessagesJson).HasColumnType("text");
        }
    }
}
