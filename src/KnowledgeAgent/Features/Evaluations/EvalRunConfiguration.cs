using KnowledgeAgent.Infrastructure.Persistence.Conventions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KnowledgeAgent.Features.Evaluations;

internal sealed class EvalRunConfiguration(bool isNpgsql) : IEntityTypeConfiguration<EvalRunRecord>
{
    public void Configure(EntityTypeBuilder<EvalRunRecord> builder)
    {
        builder.ToTable("eval_runs");
        builder.HasKey(run => run.Id);
        builder.Property(run => run.Name).HasMaxLength(200).IsRequired();
        builder.Property(run => run.Dataset).HasMaxLength(200).IsRequired();
        builder.Property(run => run.DatasetSource).HasMaxLength(20).IsRequired();
        builder.Property(run => run.Status).HasMaxLength(20).IsRequired();
        builder.Property(run => run.ChatModel).HasMaxLength(300);
        builder.Property(run => run.EmbeddingModel).HasMaxLength(300);
        builder.Property(run => run.LangfuseExperimentId).HasMaxLength(100);
        builder.Property(run => run.LangfuseUrl).HasMaxLength(2000);

        var jsonColumns = new[]
        {
            builder.Property(run => run.RequestJson).HasConversion(JsonColumns.JsonText).IsRequired(),
            builder.Property(run => run.ConfigJson!).HasConversion(JsonColumns.JsonText),
            builder.Property(run => run.MetricsJson!).HasConversion(JsonColumns.JsonText),
            builder.Property(run => run.ByTagJson!).HasConversion(JsonColumns.JsonText),
            builder.Property(run => run.GateJson!).HasConversion(JsonColumns.JsonText),
            builder.Property(run => run.ItemsJson!).HasConversion(JsonColumns.JsonText),
            builder.Property(run => run.WarningsJson!).HasConversion(JsonColumns.JsonText),
        };
        if (isNpgsql)
        {
            foreach (var column in jsonColumns)
            {
                column.HasColumnType("jsonb");
            }
        }

        builder.HasIndex(run => run.CreatedAt);
        builder.HasIndex(run => new { run.Dataset, run.CreatedAt });
        builder.HasIndex(run => run.Status);
    }
}
