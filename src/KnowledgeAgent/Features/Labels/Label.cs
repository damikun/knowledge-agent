using KnowledgeAgent.Features.Chat;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KnowledgeAgent.Features.Labels;

public sealed class Label
{
    public const int MaxNameLength = 40;
    public const int MaxDescriptionLength = 500;

    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Name { get; set; } = string.Empty;

    public string NormalizedName { get; set; } = string.Empty;

    public string Color { get; set; } = LabelColors.Gray;
    public string Description { get; set; } = string.Empty;
    public bool AutoApply { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public static string Normalize(string name) => name.Trim().ToUpperInvariant();
}

public enum LabelSource
{
    Manual,
    Auto,
}

public sealed class ConversationLabel
{
    public Guid ConversationId { get; set; }
    public Guid LabelId { get; set; }
    public LabelSource Source { get; set; }
    public bool Removed { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public static class LabelColors
{
    public const string Gray = "gray";

    public static readonly IReadOnlyList<string> All = [Gray, "blue", "green", "amber", "red", "purple", "teal", "pink"];

    public static bool IsValid(string? color) => color is not null && All.Contains(color, StringComparer.Ordinal);
}

internal sealed class LabelConfiguration : IEntityTypeConfiguration<Label>
{
    public void Configure(EntityTypeBuilder<Label> builder)
    {
        builder.ToTable("labels");
        builder.HasKey(label => label.Id);
        builder.Property(label => label.Name).HasMaxLength(Label.MaxNameLength);
        builder.Property(label => label.NormalizedName).HasMaxLength(Label.MaxNameLength);
        builder.Property(label => label.Color).HasMaxLength(16);
        builder.Property(label => label.Description).HasMaxLength(Label.MaxDescriptionLength);
        builder.HasIndex(label => label.NormalizedName).IsUnique();
    }
}

internal sealed class ConversationLabelConfiguration : IEntityTypeConfiguration<ConversationLabel>
{
    public void Configure(EntityTypeBuilder<ConversationLabel> builder)
    {
        builder.ToTable("conversation_labels");
        builder.HasKey(item => new { item.ConversationId, item.LabelId });
        builder.Property(item => item.Source).HasMaxLength(8);

        builder.HasOne<Conversation>().WithMany().HasForeignKey(item => item.ConversationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Label>().WithMany().HasForeignKey(item => item.LabelId).OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(item => new { item.LabelId, item.ConversationId });
    }
}

// Seeded once by the migration so deletions stick; the in-memory store gets them from LabelSeedInitializer.
internal static class LabelDefaults
{
    public static readonly IReadOnlyList<(Guid Id, string Name, string Color, string Description)> All =
    [
        (
            new Guid("019a5a00-0000-7000-8000-000000000001"),
            "Question",
            "blue",
            "The person asks how something works, where to find something or whether something is possible. Nothing is broken."),
        (
            new Guid("019a5a00-0000-7000-8000-000000000002"),
            "Problem",
            "red",
            "Something does not work as expected and the person needs it fixed: an error, a bug, a failed payment, or trouble with an order or an account."),
        (
            new Guid("019a5a00-0000-7000-8000-000000000003"),
            "Feedback",
            "green",
            "The person shares an opinion about the product or the service: praise, a complaint or a suggestion, without needing an answer to a question."),
    ];
}
