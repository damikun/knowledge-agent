using System.Text.Json;
using System.Text.Json.Serialization;
using KnowledgeAgent.Features.Sources.Crawling;
using KnowledgeAgent.Features.Sources.Folders;
using KnowledgeAgent.Infrastructure.Persistence.Conventions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KnowledgeAgent.Features.Sources;

internal sealed class SourceConfiguration(bool isNpgsql) : IEntityTypeConfiguration<Source>
{
    public void Configure(EntityTypeBuilder<Source> builder)
    {
        builder.ToTable("sources");
        builder.HasKey(source => source.Id);

        var metadata = builder.Property(source => source.Metadata)
            .HasConversion(JsonColumns.StringDictionary, JsonColumns.StringDictionaryComparer)
            .IsRequired();
        if (isNpgsql)
        {
            metadata.HasColumnType("jsonb");
        }

        builder.Property(source => source.Site).HasMaxLength(SourceDefaults.MaxSiteLength);

        var crawl = builder.Property(source => source.Crawl).HasConversion(
            settings => CrawlSettingsJson.Serialize(settings!),
            json => CrawlSettingsJson.Deserialize(json));
        if (isNpgsql)
        {
            crawl.HasColumnType("jsonb");
        }

        builder.HasIndex(source => source.Status);
        builder.HasIndex(source => source.CreatedAt);
        builder.HasIndex(source => source.Site);

        builder.HasIndex(source => source.ParentId);
        builder.HasOne<Source>()
            .WithMany()
            .HasForeignKey(source => source.ParentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<SourceFolder>()
            .WithMany()
            .HasForeignKey(source => source.FolderId)
            .OnDelete(DeleteBehavior.SetNull);

        if (isNpgsql)
        {
            // Trigram GIN indexes back the case-insensitive substring search (ILIKE '%term%') and need pg_trgm.
            builder.HasIndex(source => source.Title).HasMethod("gin").HasOperators("gin_trgm_ops");
            builder.HasIndex(source => source.Url).HasMethod("gin").HasOperators("gin_trgm_ops");
            builder.HasIndex(source => source.FileName).HasMethod("gin").HasOperators("gin_trgm_ops");
        }
    }
}

internal static class CrawlSettingsJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static string Serialize(WebsiteCrawlSettings settings) => JsonSerializer.Serialize(settings, Options);

    public static WebsiteCrawlSettings? Deserialize(string json) => JsonSerializer.Deserialize<WebsiteCrawlSettings>(json, Options);
}

internal sealed class SourceContentConfiguration(bool isNpgsql) : IEntityTypeConfiguration<SourceContent>
{
    public void Configure(EntityTypeBuilder<SourceContent> builder)
    {
        builder.ToTable("source_contents");
        builder.HasKey(content => content.SourceId);

        builder.HasOne<Source>()
            .WithOne()
            .HasForeignKey<SourceContent>(content => content.SourceId)
            .OnDelete(DeleteBehavior.Cascade);

        if (isNpgsql)
        {
            builder.Property(content => content.RawBytes).HasColumnType("bytea");
            builder.Property(content => content.ExtractedText).HasColumnType("text");
        }
    }
}
