using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KnowledgeAgent.Features.Staff;

public sealed class StaffMember
{
    public const int MaxIdLength = 200;
    public const int MaxNameLength = 200;

    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Email { get; set; }

    public string Role { get; set; } = string.Empty;
    public DateTimeOffset FirstSeenAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
}

internal sealed class StaffMemberConfiguration : IEntityTypeConfiguration<StaffMember>
{
    public void Configure(EntityTypeBuilder<StaffMember> builder)
    {
        builder.ToTable("staff_members");
        builder.HasKey(member => member.Id);
        builder.Property(member => member.Id).HasMaxLength(StaffMember.MaxIdLength);
        builder.Property(member => member.Name).HasMaxLength(StaffMember.MaxNameLength);
        builder.Property(member => member.Email).HasMaxLength(320);
        builder.Property(member => member.Role).HasMaxLength(16);
    }
}
