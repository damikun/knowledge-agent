using System.Security.Claims;

namespace KnowledgeAgent.Infrastructure.Security;

public interface ISignedInUserHook
{
    Task SeenAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default);
}
