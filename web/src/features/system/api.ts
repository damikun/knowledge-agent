import { useQuery } from '@tanstack/react-query';
import { call } from '../../lib/api';
import { endpoints } from '../../lib/endpoints';
import { systemInfoQueryKey } from '../../lib/queryKeys';
import type { JobsOverviewDto, SystemHealthDto, SystemInfoDto } from './types';

const jobKeys = {
  overview: ['jobs', 'overview'] as const,
  health: ['system', 'health'] as const,
};

export const jobsApi = {
  getJobsOverview: (signal?: AbortSignal): Promise<JobsOverviewDto> => call(endpoints.system.jobs, { signal }),
  runSchedule: (name: string): Promise<void> => call(endpoints.system.runSchedule, { params: { name } }),
  getSystemHealth: (signal?: AbortSignal): Promise<SystemHealthDto> => call(endpoints.system.health, { signal }),
};

export function useSystemInfo({ enabled = true }: { enabled?: boolean } = {}) {
  return useQuery({
    queryKey: systemInfoQueryKey,
    queryFn: ({ signal }): Promise<SystemInfoDto> => call(endpoints.system.info, { signal }),
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

export function useSystemHealth() {
  return useQuery({
    queryKey: jobKeys.health,
    queryFn: ({ signal }) => jobsApi.getSystemHealth(signal),
    refetchInterval: (query) => (query.state.status === 'error' ? false : 30_000),
    retry: false,
  });
}
