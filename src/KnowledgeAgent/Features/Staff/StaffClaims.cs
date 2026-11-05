using System.Security.Claims;
using KnowledgeAgent.Infrastructure.Security;

namespace KnowledgeAgent.Features.Staff;

public static class StaffClaims
{
    public static string? Id(ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);
        var id = user.FindFirst("sub")?.Value?.Trim();
        return string.IsNullOrEmpty(id) || id.Length > StaffMember.MaxIdLength ? null : id;
    }

    public static string DisplayName(ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);
        var name = user.FindFirst("name")?.Value ?? user.FindFirst("preferred_username")?.Value ?? user.FindFirst("email")?.Value ?? user.Identity?.Name;
        name = name?.Trim();
        return string.IsNullOrEmpty(name) ? "Support" : name.Length > StaffMember.MaxNameLength ? name[..StaffMember.MaxNameLength] : name;
    }

    public static string? Email(ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);
        var email = user.FindFirst("email")?.Value?.Trim();
        return string.IsNullOrEmpty(email) || email.Length > 320 ? null : email;
    }

    public static string Role(ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return user.FindAll("roles").Any(claim => claim.Value == AuthRoles.Admin) ? AuthRoles.Admin : AuthRoles.Support;
    }

    public static StaffRef? Ref(ClaimsPrincipal user) => Id(user) is { } id ? new StaffRef(id, DisplayName(user)) : null;
}

// Id is null for names from Slack or email, which have no portal account.
public sealed record StaffRef(string? Id, string Name);
