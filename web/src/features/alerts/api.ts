import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { asArray, isRecord, request } from '../../lib/api';
import { ALL_ALERT_KINDS } from './labels';
import type { AlertDto, AlertKind, AlertListDto, AlertSettingsDto, AlertSeverity, SaveAlertSettingsRequest } from './types';

export const ALERTS_REFRESH_MS = 60_000;

const alertKeys = {
  list: ['alerts', 'list'] as const,
  settings: ['alerts', 'settings'] as const,
};

const SEVERITIES: readonly AlertSeverity[] = ['info', 'warning', 'critical'];

function isKind(value: unknown): value is AlertKind {
  return ALL_ALERT_KINDS.some((kind) => kind === value);
}

function text(value: unknown, fallback = ''): string {
  return typeof value === 'string' ? value : fallback;
}

/** Unknown kinds from a newer API still show; they only miss a label in the settings. */
export function toAlert(value: unknown): AlertDto {
  const dto = isRecord(value) ? value : {};
  return {
    key: text(dto.key),
    kind: (typeof dto.kind === 'string' ? dto.kind : 'sources_failed') as AlertKind,
    severity: SEVERITIES.find((severity) => severity === dto.severity) ?? 'warning',
    title: text(dto.title, 'Something needs attention'),
    detail: text(dto.detail),
    count: typeof dto.count === 'number' && Number.isFinite(dto.count) ? dto.count : 1,
    since: text(dto.since),
    // Only portal paths: a link from the API never leaves the portal.
    link: typeof dto.link === 'string' && dto.link.startsWith('/') && !dto.link.startsWith('//') ? dto.link : '/home',
  };
}

function toAlertList(value: unknown): AlertListDto {
  const dto = isRecord(value) ? value : {};
  return {
    alerts: asArray<unknown>(dto.alerts).map(toAlert).filter((alert) => alert.key !== ''),
    snoozed: typeof dto.snoozed === 'number' ? dto.snoozed : 0,
  };
}

function toAlertSettings(value: unknown): AlertSettingsDto {
  const dto = isRecord(value) ? value : {};
  return {
    slack: dto.slack === true,
    email: dto.email === true,
    kinds: asArray<unknown>(dto.kinds).filter(isKind),
    waitingMinutes: typeof dto.waitingMinutes === 'number' && dto.waitingMinutes > 0 ? dto.waitingMinutes : 5,
    slackReady: dto.slackReady === true,
    emailReady: dto.emailReady === true,
    slackChannel: typeof dto.slackChannel === 'string' && dto.slackChannel ? dto.slackChannel : null,
    emailTo: typeof dto.emailTo === 'string' && dto.emailTo ? dto.emailTo : null,
  };
}

const alertsApi = {
  list: async (signal?: AbortSignal) => toAlertList(await request<unknown>('/api/alerts', { signal })),
  snooze: async (key: string, hours: number | null) => {
    const path = `/api/alerts/${encodeURIComponent(key)}`;
    await (hours === null ? request<unknown>(`${path}/dismiss`, { method: 'POST' }) : request<unknown>(`${path}/snooze`, { method: 'POST', body: { hours } }));
  },
  getSettings: async (signal?: AbortSignal) => toAlertSettings(await request<unknown>('/api/alerts/settings', { signal })),
  saveSettings: async (body: SaveAlertSettingsRequest) => toAlertSettings(await request<unknown>('/api/alerts/settings', { method: 'PUT', body })),
};

/** Polls every minute while the tab is visible, and on focus. The bell and Home share this query. */
export function useAlerts(enabled = true) {
  return useQuery({
    queryKey: alertKeys.list,
    queryFn: ({ signal }) => alertsApi.list(signal),
    enabled,
    staleTime: 30_000,
    refetchInterval: ALERTS_REFRESH_MS,
    refetchIntervalInBackground: false,
    refetchOnWindowFocus: true,
  });
}

/** Hides an alert for some hours, or until it changes when hours is null. Admin only. */
export function useSnoozeAlert() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ key, hours }: { key: string; hours: number | null }) => alertsApi.snooze(key, hours),
    onSuccess: (_result, { key }) => {
      // Gone at once, before the list comes back.
      queryClient.setQueryData<AlertListDto>(alertKeys.list, (old) =>
        old ? { alerts: old.alerts.filter((alert) => alert.key !== key), snoozed: old.snoozed + 1 } : old,
      );
    },
    onSettled: () => queryClient.invalidateQueries({ queryKey: alertKeys.list }),
  });
}

export function useAlertSettings(enabled = true) {
  return useQuery({ queryKey: alertKeys.settings, queryFn: ({ signal }) => alertsApi.getSettings(signal), staleTime: 30_000, enabled });
}

export function useSaveAlertSettings() {
  const queryClient = useQueryClient();
  return useMutation({
    // One save at a time, in the order the changes were made.
    scope: { id: 'alert-settings' },
    mutationFn: (body: SaveAlertSettingsRequest) => alertsApi.saveSettings(body),
    onSuccess: (saved) => {
      queryClient.setQueryData(alertKeys.settings, saved);
      // The waiting threshold changes which alerts are active.
      void queryClient.invalidateQueries({ queryKey: alertKeys.list });
    },
  });
}
