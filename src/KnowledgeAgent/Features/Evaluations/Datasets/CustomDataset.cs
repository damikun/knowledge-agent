using KnowledgeAgent.Infrastructure.Persistence.Conventions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KnowledgeAgent.Features.Evaluations.Datasets;

// Langfuse datasets can't be renamed or deleted through its API, so these live in PostgreSQL.
public sealed class CustomDatasetRecord
{
    public const int MaxNameLength = 100;
    public const int MaxDescriptionLength = 2000;

    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Name { get; set; } = string.Empty;

    // The name lower-cased, so names are unique ignoring case in both stores.
    public string NormalizedName { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string? CopiedFrom { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public static string Normalize(string name) => name.Trim().ToLowerInvariant();
}

public sealed class CustomDatasetItemRecord
{
    public const int MaxQuestionLength = 4000;
    public const int MaxAnswerLength = 20000;

    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid DatasetId { get; set; }
    public string Question { get; set; } = string.Empty;
    public string? ReferenceAnswer { get; set; }
    public List<string> ExpectedFacts { get; set; } = [];
    public List<string> ExpectedSources { get; set; } = [];

    public bool Answerable { get; set; } = true;
    public List<string> Tags { get; set; } = [];

    // No foreign keys: the question stays when retention deletes the conversation.
    public Guid? ConversationId { get; set; }
    public Guid? MessageId { get; set; }

    // Null on older rows, read as manual or conversation from ConversationId.
    public string? Source { get; set; }

    public EvalItemHistory? History { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public static class CustomDatasetItemSources
{
    public const string Manual = "manual";
    public const string Conversation = "conversation";
    public const string Upload = "upload";
    public const string Copy = "copy";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal) { Manual, Conversation, Upload, Copy };

    public static string Of(CustomDatasetItemRecord item) =>
        item.Source ?? (item.ConversationId is null ? Manual : Conversation);
}

public static class EvalTurnRoles
{
    public const string User = "user";
    public const string Assistant = "assistant";
}

public sealed record EvalTurn(string Role, string Content);

// A class around the list so it maps to one JSON document column.
public sealed class EvalItemHistory
{
    public List<EvalTurn> Turns { get; set; } = [];
}

internal sealed class CustomDatasetConfiguration : IEntityTypeConfiguration<CustomDatasetRecord>
{
    public void Configure(EntityTypeBuilder<CustomDatasetRecord> builder)
    {
        builder.ToTable("eval_datasets");
        builder.HasKey(dataset => dataset.Id);
        builder.Property(dataset => dataset.Name).HasMaxLength(CustomDatasetRecord.MaxNameLength).IsRequired();
        builder.Property(dataset => dataset.NormalizedName).HasMaxLength(CustomDatasetRecord.MaxNameLength).IsRequired();
        builder.Property(dataset => dataset.Description).HasMaxLength(CustomDatasetRecord.MaxDescriptionLength);
        builder.Property(dataset => dataset.CopiedFrom).HasMaxLength(300);
        builder.HasIndex(dataset => dataset.NormalizedName).IsUnique();
        builder.HasMany<CustomDatasetItemRecord>()
            .WithOne()
            .HasForeignKey(item => item.DatasetId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class CustomDatasetItemConfiguration(bool isNpgsql) : IEntityTypeConfiguration<CustomDatasetItemRecord>
{
    public void Configure(EntityTypeBuilder<CustomDatasetItemRecord> builder)
    {
        builder.ToTable("eval_dataset_items");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Question).HasMaxLength(CustomDatasetItemRecord.MaxQuestionLength).IsRequired();
        builder.Property(item => item.ReferenceAnswer).HasMaxLength(CustomDatasetItemRecord.MaxAnswerLength);
        builder.Property(item => item.Source).HasMaxLength(20);
        var history = builder.Property(item => item.History)
            .HasConversion(JsonColumns.Document<EvalItemHistory>()!, JsonColumns.DocumentComparer<EvalItemHistory>());
        builder.HasIndex(item => new { item.DatasetId, item.CreatedAt });

        var lists = new[]
        {
            builder.Property(item => item.ExpectedFacts).HasConversion(JsonColumns.StringList, JsonColumns.StringListComparer).IsRequired(),
            builder.Property(item => item.ExpectedSources).HasConversion(JsonColumns.StringList, JsonColumns.StringListComparer).IsRequired(),
            builder.Property(item => item.Tags).HasConversion(JsonColumns.StringList, JsonColumns.StringListComparer).IsRequired(),
        };
        if (isNpgsql)
        {
            foreach (var list in lists)
            {
                list.HasColumnType("jsonb");
            }

            history.HasColumnType("jsonb");
        }
    }
}
