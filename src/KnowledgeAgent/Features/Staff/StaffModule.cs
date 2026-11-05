using KnowledgeAgent.Infrastructure.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace KnowledgeAgent.Features.Staff;

public static class StaffModule
{
    public static IHostApplicationBuilder AddStaff(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.TryAddScoped<StaffDirectory>();
        builder.Services.TryAddScoped<IStaffDirectory>(provider => provider.GetRequiredService<StaffDirectory>());
        builder.Services.AddScoped<ISignedInUserHook>(provider => provider.GetRequiredService<StaffDirectory>());
        builder.Services.AddOptions<TeamOptions>().BindConfiguration(TeamOptions.SectionName);
        builder.Services.TryAddScoped<TeamService>();
        return builder;
    }
}
