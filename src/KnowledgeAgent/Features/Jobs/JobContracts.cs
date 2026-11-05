namespace KnowledgeAgent.Features.Jobs;

public sealed record JobScheduleDto(string Name, string Kind, string Cron, bool Enabled, DateTimeOffset? NextRunAt);

public sealed record JobsOverviewDto(string Engine, string? DashboardUrl, IReadOnlyList<JobScheduleDto> Schedules);
