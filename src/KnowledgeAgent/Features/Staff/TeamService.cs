using KnowledgeAgent.Infrastructure.Persistence;
using KnowledgeAgent.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KnowledgeAgent.Features.Staff;

public sealed record TeamMemberDto(
    string Id,
    string Name,
    string? Email,
    string Role,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt);

public sealed record TeamDto(IReadOnlyList<TeamMemberDto> Members, string? KeycloakConsoleUrl, bool RoleChanges);

internal sealed class TeamService(
    KnowledgeDbContext db,
    DbContextGate gate,
    IOptions<TeamOptions> teamOptions,
    IOptions<AuthOptions> authOptions)
{
    public async Task<TeamDto> GetAsync(CancellationToken cancellationToken = default)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        var members = await db.StaffMembers.AsNoTracking().ToListAsync(cancellationToken);
        var rows = members
            .OrderBy(member => member.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(member => member.Id, StringComparer.Ordinal)
            .Select(member => new TeamMemberDto(member.Id, member.Name, member.Email, member.Role, member.FirstSeenAt, member.LastSeenAt))
            .ToList();
        return new TeamDto(rows, TeamOptions.ResolveConsoleUrl(teamOptions.Value, authOptions.Value), RoleChanges: false);
    }
}
