using KnowledgeAgent.Features.People;
using KnowledgeAgent.Infrastructure.Persistence.Conventions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KnowledgeAgent.Features.Widgets;

internal sealed class WidgetConfiguration(bool isNpgsql) : IEntityTypeConfiguration<Widget>
{
    public void Configure(EntityTypeBuilder<Widget> builder)
    {
        builder.ToTable("widgets");
        builder.HasKey(widget => widget.Id);
        builder.HasIndex(widget => widget.PublicKey).IsUnique();
        builder.HasIndex(widget => widget.AgentId);
        builder.Property(widget => widget.PublicKey).HasMaxLength(40);
        builder.Property(widget => widget.Name).HasMaxLength(Widget.MaxNameLength);

        var settings = builder.Property(widget => widget.Settings)
            .HasConversion(JsonColumns.Document<WidgetSettings>(), JsonColumns.DocumentComparer<WidgetSettings>())
            .IsRequired();
        if (isNpgsql)
        {
            settings.HasColumnType("jsonb");
        }
    }
}

internal sealed class WidgetAssetConfiguration : IEntityTypeConfiguration<WidgetAsset>
{
    public void Configure(EntityTypeBuilder<WidgetAsset> builder)
    {
        builder.ToTable("widget_assets");
        builder.HasKey(asset => asset.Id);
        builder.Property(asset => asset.ContentType).HasMaxLength(32);
        builder.Property(asset => asset.Sha256).HasMaxLength(64);
        builder.HasOne<Widget>().WithMany().HasForeignKey(asset => asset.WidgetId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class WidgetVisitorConfiguration : IEntityTypeConfiguration<WidgetVisitor>
{
    public void Configure(EntityTypeBuilder<WidgetVisitor> builder)
    {
        builder.ToTable("widget_visitors");
        builder.HasKey(visitor => visitor.Id);
        builder.Property(visitor => visitor.Name).HasMaxLength(WidgetVisitor.MaxNameLength);
        builder.Property(visitor => visitor.Email).HasMaxLength(WidgetVisitor.MaxEmailLength);
        builder.Property(visitor => visitor.LastPageUrl).HasMaxLength(Contact.MaxUrlLength);
        builder.Property(visitor => visitor.LastReferrer).HasMaxLength(Contact.MaxUrlLength);
        builder.Property(visitor => visitor.Language).HasMaxLength(Contact.MaxLanguageLength);
        builder.HasOne<Widget>().WithMany().HasForeignKey(visitor => visitor.WidgetId).OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Contact>().WithMany().HasForeignKey(visitor => visitor.ContactId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class WidgetDailyUsageConfiguration : IEntityTypeConfiguration<WidgetDailyUsage>
{
    public void Configure(EntityTypeBuilder<WidgetDailyUsage> builder)
    {
        builder.ToTable("widget_daily_usage");
        builder.HasKey(usage => new { usage.WidgetId, usage.Day });
        builder.HasOne<Widget>().WithMany().HasForeignKey(usage => usage.WidgetId).OnDelete(DeleteBehavior.Cascade);
    }
}
