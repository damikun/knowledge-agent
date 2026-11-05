using System.Security.Claims;
using KnowledgeAgent.Infrastructure.Persistence;
using KnowledgeAgent.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace KnowledgeAgent.Features.Staff;

public sealed record StaffMemberDto(string Id, string Name, string? Email, string Role, DateTimeOffset LastSeenAt);

public interface IStaffDirectory
{
    Task SeenAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default);

    Task<StaffMember?> GetAsync(string id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StaffMemberDto>> ListAsync(CancellationToken cancellationToken = default);
}

internal sealed class StaffDirectory(KnowledgeDbContext db, DbContextGate gate, TimeProvider timeProvider) : IStaffDirectory, ISignedInUserHook
{
    // The portal asks who is signed in on every load; an unchanged row is written at most this often.
    internal static readonly TimeSpan SeenInterval = TimeSpan.FromMinutes(5);

    public async Task SeenAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        // Only staff can be assigned conversations; a signed-in user without a role is not one.
        if (user.Identity?.IsAuthenticated != true
            || StaffClaims.Id(user) is not { } id
            || !user.FindAll("roles").Any(claim => claim.Value is AuthRoles.Admin or AuthRoles.Support))
        {
            return;
        }

        var name = StaffClaims.DisplayName(user);
        var email = StaffClaims.Email(user);
        var role = StaffClaims.Role(user);
        var now = timeProvider.GetUtcNow();
        using var _ = await gate.EnterAsync(cancellationToken);
        try
        {
            var member = await db.StaffMembers.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
            if (member is null)
            {
                db.StaffMembers.Add(new StaffMember { Id = id, Name = name, Email = email, Role = role, FirstSeenAt = now, LastSeenAt = now });
            }
            else if (member.Name != name || member.Email != email || member.Role != role || now - member.LastSeenAt >= SeenInterval)
            {
                member.Name = name;
                member.Email = email;
                member.Role = role;
                member.LastSeenAt = now;
            }
            else
            {
                return;
            }

            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Two first requests of a new user at once: the other one added the row.
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    public async Task<StaffMember?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        return await db.StaffMembers.AsNoTracking().FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<StaffMemberDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        var members = await db.StaffMembers.AsNoTracking().ToListAsync(cancellationToken);
        return members
            .OrderBy(member => member.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(member => member.Id, StringComparer.Ordinal)
            .Select(member => new StaffMemberDto(member.Id, member.Name, member.Email, member.Role, member.LastSeenAt))
            .ToList();
    }
}
