import type { JobScheduleDto, JobsOverviewDto, SystemComponentHealthDto, SystemHealthDto } from '../features/system/types';

export const INNGEST_DASHBOARD_URL = 'http://localhost:8288';

export function refreshSchedule(overrides: Partial<JobScheduleDto> = {}): JobScheduleDto {
  return {
    name: 'refresh-web-sources',
    kind: 'ingestion.refresh-web',
    cron: '0 4 * * *',
    enabled: false,
    nextRunAt: null,
    ...overrides,
  };
}

/** GET /api/jobs/overview with the in-process engine, as in in-memory mode and the tests. */
export function jobsOverview(overrides: Partial<JobsOverviewDto> = {}): JobsOverviewDto {
  return { engine: 'inprocess', dashboardUrl: null, schedules: [refreshSchedule()], ...overrides };
}

export function inngestOverview(overrides: Partial<JobsOverviewDto> = {}): JobsOverviewDto {
  return jobsOverview({ engine: 'inngest', dashboardUrl: INNGEST_DASHBOARD_URL, ...overrides });
}

const CHECKED = '2026-11-05T10:00:00Z';

export function healthComponent(key: string, status: SystemComponentHealthDto['status'], variant: string | null = null, detail = `${key} detail`): SystemComponentHealthDto {
  return { key, status, detail, checkedAt: CHECKED, variant };
}

/** What the API reports for the Docker Compose setup with everything working. */
export function healthyInstallation(overrides: SystemComponentHealthDto[] = []): SystemHealthDto {
  const components = [
    healthComponent('api', 'healthy'),
    healthComponent('database', 'healthy', 'postgres', 'Connected'),
    healthComponent('vectorStore', 'healthy', 'clickhouse', 'Search index reachable'),
    healthComponent('jobs', 'healthy', 'inngest', 'Connected; jobs survive restarts'),
    healthComponent('signIn', 'healthy', 'oidc', 'Reachable; staff can sign in'),
    healthComponent('langfuse', 'healthy', null, 'Reachable, version 4.1.0'),
    healthComponent('models', 'healthy', null, 'Answers: anthropic, claude-opus-5-5. Embeddings: voyage, voyage-3.5.'),
    healthComponent('email', 'healthy', null, 'Sends requests and reads replies'),
    healthComponent('slack', 'notConfigured', null, 'Not set up; requests for a person stay in the Inbox'),
  ];
  const byKey = new Map(components.map((c) => [c.key, c]));
  for (const override of overrides) {
    byKey.set(override.key, override);
  }
  return { components: [...byKey.values()] };
}
