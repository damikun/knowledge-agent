using KnowledgeAgent.Features.Chat;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace KnowledgeAgent.Features.People;

public static class PeopleModule
{
    public static IHostApplicationBuilder AddPeople(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.TryAddScoped<IPeopleRepository, PeopleRepository>();
        builder.Services.TryAddScoped<ISegmentService, SegmentService>();
        builder.Services.TryAddSingleton<SegmentCountCache>();
        builder.Services.TryAddEnumerable(ServiceDescriptor.Scoped<IVisitorProfile, ContactVisitorProfile>());
        return builder;
    }
}

internal sealed class ContactVisitorProfile(IPeopleRepository people) : IVisitorProfile
{
    public Task<IReadOnlyList<VisitorFact>> FactsAsync(Guid contactId, CancellationToken cancellationToken) => people.AgentFactsAsync(contactId, cancellationToken);
}
