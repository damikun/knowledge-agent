using KnowledgeAgent.Infrastructure.Jobs;
using KnowledgeAgent.Infrastructure.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace KnowledgeAgent.Features.Jobs;

internal static class JobEndpoints
{
    public static RouteGroupBuilder MapJobEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/jobs").WithTags("Jobs");

        group.MapGet("/overview", GetOverview)
            .WithName("GetJobsOverview")
            .WithSummary("The engine that runs the jobs, its dashboard, and the cron schedules with their next run.");

        group.MapPost("/schedules/{name}/run", RunScheduleAsync)
            .WithName("RunJobSchedule")
            .WithSummary("Queue the schedule's job now, whether or not the schedule is enabled.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        return group;
    }

    private static Ok<JobsOverviewDto> GetOverview(
        IEnumerable<JobScheduleRegistration> registrations,
        IOptionsMonitor<JobsOptions> options,
        JobEngineInfo engine,
        TimeProvider timeProvider)
    {
        var settings = options.CurrentValue;
        var now = timeProvider.GetUtcNow();
        var schedules = registrations
            .Select(registration => JobSchedules.Resolve(registration, settings))
            .OrderBy(schedule => schedule.Name, StringComparer.Ordinal)
            .Select(schedule => new JobScheduleDto(
                schedule.Name,
                schedule.Kind,
                schedule.Cron,
                schedule.Enabled,
                schedule.Enabled ? JobSchedules.NextOccurrence(schedule.Cron, now) : null))
            .ToArray();

        return TypedResults.Ok(new JobsOverviewDto(engine.Name, engine.DashboardUrl, schedules));
    }

    private static async Task<Results<Accepted, ProblemHttpResult>> RunScheduleAsync(
        string name,
        IEnumerable<JobScheduleRegistration> registrations,
        IJobQueue queue,
        CancellationToken cancellationToken)
    {
        var registration = registrations.FirstOrDefault(schedule => string.Equals(schedule.Name, name, StringComparison.OrdinalIgnoreCase));
        if (registration is null)
        {
            return ApiResults.NotFound($"Schedule '{name}' was not found.");
        }

        await queue.EnqueueAsync(registration.Kind, registration.Key, cancellationToken: cancellationToken);
        return TypedResults.Accepted((string?)null);
    }
}
