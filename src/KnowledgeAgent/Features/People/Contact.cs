using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KnowledgeAgent.Features.People;

public sealed class Contact
{
    public const int MaxExternalIdLength = 200;
    public const int MaxNameLength = 100;
    public const int MaxEmailLength = 254;
    public const int MaxNotesLength = 4000;
    public const int MaxUrlLength = 500;
    public const int MaxLanguageLength = 35;

    public Guid Id { get; set; } = Guid.CreateVersion7();

    // The user id of the host website, set only through verified identity, so it can be trusted.
    public string? ExternalId { get; set; }
    public string? Name { get; set; }
    public string? Email { get; set; }
    public string? Notes { get; set; }

    public Dictionary<string, JsonElement> Attributes { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, AttributeStamp> AttributeSources { get; set; } = new(StringComparer.Ordinal);

    public int SessionCount { get; set; }
    public string? LastPageUrl { get; set; }
    public string? LastReferrer { get; set; }
    public string? Language { get; set; }

    public DateTimeOffset FirstSeenAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }

    // Kept up to date on insert and delete and recounted by the retention job, so lists need no count per row.
    public int ConversationCount { get; set; }

    public bool Identified => ExternalId is not null;

    public bool Returning => SessionCount > 1 || ConversationCount > 1;

    public string DisplayName => DisplayNameOf(Id, Name);

    public static string DisplayNameOf(Guid id, string? name) =>
        string.IsNullOrWhiteSpace(name) ? $"Visitor {id.ToString("N")[^4..].ToUpperInvariant()}" : name;
}

internal sealed class ContactConfiguration(bool isNpgsql) : IEntityTypeConfiguration<Contact>
{
    public void Configure(EntityTypeBuilder<Contact> builder)
    {
        builder.ToTable("contacts");
        builder.HasKey(contact => contact.Id);
        builder.Ignore(contact => contact.Identified);
        builder.Ignore(contact => contact.DisplayName);
        builder.Ignore(contact => contact.Returning);
        builder.Property(contact => contact.ExternalId).HasMaxLength(Contact.MaxExternalIdLength);
        builder.Property(contact => contact.Name).HasMaxLength(Contact.MaxNameLength);
        builder.Property(contact => contact.Email).HasMaxLength(Contact.MaxEmailLength);
        builder.Property(contact => contact.Notes).HasMaxLength(Contact.MaxNotesLength);
        builder.Property(contact => contact.LastPageUrl).HasMaxLength(Contact.MaxUrlLength);
        builder.Property(contact => contact.LastReferrer).HasMaxLength(Contact.MaxUrlLength);
        builder.Property(contact => contact.Language).HasMaxLength(Contact.MaxLanguageLength);
        var attributes = builder.Property(contact => contact.Attributes)
            .HasConversion(ContactJson.Values, ContactJson.ValuesComparer)
            .IsRequired();
        var sources = builder.Property(contact => contact.AttributeSources)
            .HasConversion(ContactJson.Stamps, ContactJson.StampsComparer)
            .IsRequired();
        if (isNpgsql)
        {
            attributes.HasColumnType("jsonb");
            sources.HasColumnType("jsonb");
        }

        builder.HasIndex(contact => contact.ExternalId).IsUnique();

        builder.HasIndex(contact => new { contact.LastSeenAt, contact.Id });
        if (isNpgsql)
        {
            // Backs the case-insensitive substring search of the People page (ILIKE '%term%'), and email lookups.
            builder.HasIndex(contact => contact.Name).HasMethod("gin").HasOperators("gin_trgm_ops");
            builder.HasIndex(contact => contact.Email).HasMethod("gin").HasOperators("gin_trgm_ops");

            // Backs attribute filters: containment (@>), existence and equality in JSON paths (@?, @@).
            builder.HasIndex(contact => contact.Attributes).HasMethod("gin").HasOperators("jsonb_path_ops");
        }
    }
}
