using System.Collections.Concurrent;
using KnowledgeAgent.Features.Labels;
using KnowledgeAgent.Infrastructure.Persistence;
using KnowledgeAgent.Infrastructure.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KnowledgeAgent.Features.People;

public sealed class Segment
{
    public const int MaxNameLength = 80;
    public const int MaxDescriptionLength = 300;
    public const int MaxSegments = 100;

    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    public string Filter { get; set; } = "[]";
    public string Color { get; set; } = LabelColors.Gray;
    public string Icon { get; set; } = SegmentIcons.Users;
    public string? CreatedById { get; set; }
    public string? CreatedByName { get; set; }
    public bool Shared { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public static class SegmentIcons
{
    public const string Users = "users";

    public static readonly IReadOnlyList<string> All = [Users, "star", "crown", "building", "rocket", "heart", "flag", "zap", "globe", "user-check"];
}

internal sealed class SegmentConfiguration(bool isNpgsql) : IEntityTypeConfiguration<Segment>
{
    public void Configure(EntityTypeBuilder<Segment> builder)
    {
        builder.ToTable("segments");
        builder.HasKey(segment => segment.Id);
        builder.Property(segment => segment.Name).HasMaxLength(Segment.MaxNameLength).IsRequired();
        builder.Property(segment => segment.Description).HasMaxLength(Segment.MaxDescriptionLength);
        var filter = builder.Property(segment => segment.Filter).IsRequired();
        if (isNpgsql)
        {
            filter.HasColumnType("jsonb");
        }

        builder.Property(segment => segment.Color).HasMaxLength(20).IsRequired();
        builder.Property(segment => segment.Icon).HasMaxLength(30).IsRequired();
        builder.Property(segment => segment.CreatedById).HasMaxLength(200);
        builder.Property(segment => segment.CreatedByName).HasMaxLength(200);
        builder.HasIndex(segment => segment.Name);
    }
}

public sealed record SegmentDto(
    Guid Id,
    string Name,
    string? Description,
    IReadOnlyList<ContactCondition> Filter,
    string Color,
    string Icon,
    bool Shared,
    string? CreatedById,
    string? CreatedByName,
    bool Mine,
    int Count,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record SaveSegmentRequest(
    string? Name = null,
    string? Description = null,
    IReadOnlyList<ContactCondition>? Filter = null,
    string? Color = null,
    string? Icon = null,
    bool? Shared = null);

public sealed record SegmentViewer(string? Id, string Name, bool Admin);

public interface ISegmentService
{
    Task<IReadOnlyList<SegmentDto>> ListAsync(SegmentViewer viewer, CancellationToken cancellationToken = default);

    Task<Segment?> FindAsync(Guid id, SegmentViewer viewer, CancellationToken cancellationToken = default);

    Task<SegmentDto> CreateAsync(SaveSegmentRequest request, SegmentViewer viewer, CancellationToken cancellationToken = default);

    Task<SegmentDto?> UpdateAsync(Guid id, SaveSegmentRequest request, SegmentViewer viewer, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ResolvedCondition>> ConditionsAsync(Segment segment, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Segment>> AllAsync(CancellationToken cancellationToken = default);
}

// Cached a minute so the Contacts page does not recount every segment on each visit.
internal sealed class SegmentCountCache(TimeProvider timeProvider)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(1);
    private readonly ConcurrentDictionary<(Guid Id, DateTimeOffset Version), (int Count, DateTimeOffset At)> _counts = new();

    public bool TryGet(Segment segment, out int count)
    {
        count = 0;
        if (_counts.TryGetValue((segment.Id, segment.UpdatedAt), out var entry) && timeProvider.GetUtcNow() - entry.At < Lifetime)
        {
            count = entry.Count;
            return true;
        }

        return false;
    }

    public void Set(Segment segment, int count)
    {
        if (_counts.Count > 1000)
        {
            _counts.Clear();
        }

        _counts[(segment.Id, segment.UpdatedAt)] = (count, timeProvider.GetUtcNow());
    }

    public void Forget(Guid id)
    {
        foreach (var key in _counts.Keys.Where(key => key.Id == id))
        {
            _counts.TryRemove(key, out _);
        }
    }
}

internal sealed class SegmentService(
    KnowledgeDbContext db,
    DbContextGate gate,
    IPeopleRepository people,
    SegmentCountCache counts,
    TimeProvider timeProvider) : ISegmentService
{
    public async Task<IReadOnlyList<SegmentDto>> ListAsync(SegmentViewer viewer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(viewer);
        List<Segment> segments;
        using (await gate.EnterAsync(cancellationToken))
        {
            segments = await Visible(viewer).AsNoTracking().ToListAsync(cancellationToken);
        }

        var registry = await people.AttributeRegistryAsync(cancellationToken);
        var result = new List<SegmentDto>();
        foreach (var segment in segments.OrderBy(segment => segment.Name, StringComparer.OrdinalIgnoreCase))
        {
            result.Add(ToDto(segment, viewer, await CountAsync(segment, registry, cancellationToken)));
        }

        return result;
    }

    public async Task<Segment?> FindAsync(Guid id, SegmentViewer viewer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(viewer);
        using var _ = await gate.EnterAsync(cancellationToken);
        return await Visible(viewer).AsNoTracking().FirstOrDefaultAsync(segment => segment.Id == id, cancellationToken);
    }

    public async Task<SegmentDto> CreateAsync(SaveSegmentRequest request, SegmentViewer viewer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(viewer);
        var registry = await people.AttributeRegistryAsync(cancellationToken);
        var errors = Validate(request, registry, creating: true);
        int existing;
        using (await gate.EnterAsync(cancellationToken))
        {
            existing = await db.Segments.CountAsync(cancellationToken);
        }

        if (existing >= Segment.MaxSegments)
        {
            errors.Add("name", $"There can be at most {Segment.MaxSegments} segments; delete one first.");
        }

        if (errors.HasErrors)
        {
            throw new ValidationFailedException(errors.ToDictionary());
        }

        var now = timeProvider.GetUtcNow();
        var segment = new Segment
        {
            Name = request.Name!.Trim(),
            Description = Description(request.Description),
            Filter = ContactFilters.Serialize(request.Filter ?? []),
            Color = request.Color ?? LabelColors.Gray,
            Icon = request.Icon ?? SegmentIcons.Users,
            Shared = request.Shared ?? true,
            CreatedById = viewer.Id,
            CreatedByName = viewer.Name,
            CreatedAt = now,
            UpdatedAt = now,
        };
        using (await gate.EnterAsync(cancellationToken))
        {
            try
            {
                db.Segments.Add(segment);
                await db.SaveChangesAsync(cancellationToken);
            }
            finally
            {
                db.ChangeTracker.Clear();
            }
        }

        return ToDto(segment, viewer, await CountAsync(segment, registry, cancellationToken));
    }

    public async Task<SegmentDto?> UpdateAsync(Guid id, SaveSegmentRequest request, SegmentViewer viewer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(viewer);
        var registry = await people.AttributeRegistryAsync(cancellationToken);
        Segment? segment;
        using (await gate.EnterAsync(cancellationToken))
        {
            try
            {
                segment = await db.Segments.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
                if (segment is null || !(viewer.Admin || (segment.CreatedById is not null && segment.CreatedById == viewer.Id) || segment.Shared))
                {
                    return null;
                }

                var errors = Validate(request, registry, creating: false);
                if (errors.HasErrors)
                {
                    throw new ValidationFailedException(errors.ToDictionary());
                }

                segment.Name = request.Name?.Trim() ?? segment.Name;
                segment.Description = request.Description is null ? segment.Description : Description(request.Description);
                segment.Filter = request.Filter is null ? segment.Filter : ContactFilters.Serialize(request.Filter);
                segment.Color = request.Color ?? segment.Color;
                segment.Icon = request.Icon ?? segment.Icon;

                // Only its creator or an admin makes a segment private again.
                if (request.Shared is { } shared && (viewer.Admin || segment.CreatedById == viewer.Id))
                {
                    segment.Shared = shared;
                }

                segment.UpdatedAt = timeProvider.GetUtcNow();
                await db.SaveChangesAsync(cancellationToken);
            }
            finally
            {
                db.ChangeTracker.Clear();
            }
        }

        counts.Forget(id);
        return ToDto(segment, viewer, await CountAsync(segment, registry, cancellationToken));
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        try
        {
            if (await db.Segments.FirstOrDefaultAsync(segment => segment.Id == id, cancellationToken) is not { } segment)
            {
                return false;
            }

            db.Segments.Remove(segment);
            await db.SaveChangesAsync(cancellationToken);
            counts.Forget(id);
            return true;
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    public async Task<IReadOnlyList<ResolvedCondition>> ConditionsAsync(Segment segment, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(segment);
        return ContactFilters.Resolve(ContactFilters.Deserialize(segment.Filter), await people.AttributeRegistryAsync(cancellationToken), strict: false, []);
    }

    public async Task<IReadOnlyList<Segment>> AllAsync(CancellationToken cancellationToken = default)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        return (await db.Segments.AsNoTracking().ToListAsync(cancellationToken)).OrderBy(segment => segment.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private IQueryable<Segment> Visible(SegmentViewer viewer) =>
        viewer.Admin ? db.Segments : db.Segments.Where(segment => segment.Shared || (segment.CreatedById != null && segment.CreatedById == viewer.Id));

    private async Task<int> CountAsync(Segment segment, IReadOnlyDictionary<string, ContactAttributeDefinition> registry, CancellationToken cancellationToken)
    {
        if (counts.TryGet(segment, out var cached))
        {
            return cached;
        }

        var conditions = ContactFilters.Resolve(ContactFilters.Deserialize(segment.Filter), registry, strict: false, []);
        var count = await people.CountAsync(new PeopleQuery { Conditions = conditions, Now = timeProvider.GetUtcNow() }, cancellationToken);
        counts.Set(segment, count);
        return count;
    }

    private static ValidationErrors Validate(SaveSegmentRequest request, IReadOnlyDictionary<string, ContactAttributeDefinition> registry, bool creating)
    {
        var errors = new ValidationErrors();
        var name = request.Name?.Trim();
        if ((creating || request.Name is not null) && name is not { Length: > 0 and <= Segment.MaxNameLength })
        {
            errors.Add("name", $"The name must be 1 to {Segment.MaxNameLength} characters.");
        }

        if (request.Description?.Trim() is { Length: > Segment.MaxDescriptionLength })
        {
            errors.Add("description", $"The description can be at most {Segment.MaxDescriptionLength} characters long.");
        }

        if (request.Color is not null && !LabelColors.All.Contains(request.Color))
        {
            errors.Add("color", $"color must be one of {string.Join(", ", LabelColors.All)}.");
        }

        if (request.Icon is not null && !SegmentIcons.All.Contains(request.Icon))
        {
            errors.Add("icon", $"icon must be one of {string.Join(", ", SegmentIcons.All)}.");
        }

        if (request.Filter is { } filter)
        {
            var problems = new List<string>();
            if (filter.Count > ContactFilters.MaxConditions)
            {
                problems.Add($"A filter has at most {ContactFilters.MaxConditions} conditions.");
            }

            ContactFilters.Resolve(filter, registry, strict: true, problems);
            foreach (var problem in problems)
            {
                errors.Add("filter", problem);
            }
        }

        return errors;
    }

    private static string? Description(string? description) => description?.Trim() is { Length: > 0 } text ? text : null;

    private static SegmentDto ToDto(Segment segment, SegmentViewer viewer, int count) => new(
        segment.Id,
        segment.Name,
        segment.Description,
        ContactFilters.Deserialize(segment.Filter),
        segment.Color,
        segment.Icon,
        segment.Shared,
        segment.CreatedById,
        segment.CreatedByName,
        segment.CreatedById is not null && segment.CreatedById == viewer.Id,
        count,
        segment.CreatedAt,
        segment.UpdatedAt);
}
