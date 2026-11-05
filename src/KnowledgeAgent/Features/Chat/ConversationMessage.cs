using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KnowledgeAgent.Features.Chat;

public enum MessageAuthor
{
    User,

    Visitor,

    Assistant,

    Staff,

    System,
}

public sealed class ConversationMessage
{
    public Guid Id { get; set; }
    public Guid ConversationId { get; set; }

    // Unique and increasing within the conversation; live streams resume after it.
    public int Sequence { get; set; }

    public MessageAuthor Author { get; set; }
    public string? AuthorName { get; set; }
    public string Text { get; set; } = string.Empty;
    public string CitationsJson { get; set; } = "[]";

    public Guid? TurnId { get; set; }

    public bool StaffOnly { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

internal sealed class ConversationMessageConfiguration(bool isNpgsql) : IEntityTypeConfiguration<ConversationMessage>
{
    public const string SequenceIndexName = "ix_conversation_messages_conversation_id_sequence";

    public void Configure(EntityTypeBuilder<ConversationMessage> builder)
    {
        builder.ToTable("conversation_messages");
        builder.HasKey(message => message.Id);
        builder.HasIndex(message => new { message.ConversationId, message.Sequence }).IsUnique();
        builder.Property(message => message.AuthorName).HasMaxLength(200);
        builder.Property(message => message.Author).HasMaxLength(16);

        if (isNpgsql)
        {
            builder.Property(message => message.CitationsJson).HasColumnType("jsonb");

            // Backs the search over what was said (ILIKE '%term%'), in any language, without a text search configuration.
            builder.HasIndex(message => message.Text).HasMethod("gin").HasOperators("gin_trgm_ops");
        }
    }
}

internal static class ConversationMessageMappings
{
    public static ConversationMessageDto ToDto(this ConversationMessage message) => new(
        message.Id,
        message.Sequence,
        message.Author.ToString().ToLowerInvariant(),
        message.AuthorName,
        message.Text,
        ChatJson.DeserializeDtos<CitationDto>(message.CitationsJson),
        message.TurnId,
        message.CreatedAt)
    {
        StaffOnly = message.StaffOnly,
    };
}
