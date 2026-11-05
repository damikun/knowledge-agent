using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KnowledgeAgent.Features.Sources.Folders;

internal sealed class SourceFolderConfiguration : IEntityTypeConfiguration<SourceFolder>
{
    public void Configure(EntityTypeBuilder<SourceFolder> builder)
    {
        builder.ToTable("source_folders");
        builder.HasKey(folder => folder.Id);
        builder.Property(folder => folder.Name).HasMaxLength(SourceFolder.MaxNameLength);

        // Deleting a folder deletes its subfolders; the sources foreign key then leaves their sources unfiled.
        builder.HasOne<SourceFolder>()
            .WithMany()
            .HasForeignKey(folder => folder.ParentId)
            .OnDelete(DeleteBehavior.Cascade);

        var created = new DateTimeOffset(2026, 11, 5, 0, 0, 0, TimeSpan.Zero);
        builder.HasData(new SourceFolder
        {
            Id = SourceFolder.InternalId,
            Name = SourceFolder.InternalName,
            IsInternal = true,
            CreatedAt = created,
            UpdatedAt = created,
        });
    }
}
