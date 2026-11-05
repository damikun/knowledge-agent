import { useQuery } from '@tanstack/react-query';
import { request } from '../../lib/api';
import { systemInfoQueryKey } from '../../lib/queryKeys';
import type { JobsOverviewDto, SystemHealthDto, SystemInfoDto } from './types';

const jobKeys = {
  overview: ['jobs', 'overview'] as const,
  health: ['system', 'health'] as const,
};

export const jobsApi = {
  getJobsOverview: (signal?: AbortSignal) => request<JobsOverviewDto>('/api/jobs/overview', { signal }),
  runSchedule: (name: string) => request<void>(`/api/jobs/schedules/${encodeURIComponent(name)}/run`, { method: 'POST' }),
  getSystemHealth: (signal?: AbortSignal) => request<SystemHealthDto>('/api/system/health', { signal }),
};

export function useSystemInfo({ enabled = true }: { enabled?: boolean } = {}) {
  return useQuery({
    queryKey: systemInfoQueryKey,
    queryFn: ({ signal }) => request<SystemInfoDto>('/api/system/info', { signal }),
    staleTime: 60_000,
    refetchInterval: (query) => (query.state.status === 'error' ? 15_000 : false),
    enabled,
  });
}

export function useJobsOverview() {
  return useQuery({
    queryKey: jobKeys.overview,
    queryFn: ({ signal }) => jobsApi.getJobsOverview(signal),
  });
}

/** The API caches checks for 20 s, so polling every 30 s stays current without extra work. */
export function useSystemHealth() {
  return useQuery({
    queryKey: jobKeys.health,
    queryFn: ({ signal }) => jobsApi.getSystemHealth(signal),
    refetchInterval: (query) => (query.state.status === 'error' ? false : 30_000),
    retry: false,
  });
}
