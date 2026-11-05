import type { AlertDto, AlertListDto, AlertSettingsDto } from '../features/alerts/types';
import { jsonResponse } from './utils';

export function alertDto(overrides: Partial<AlertDto> = {}): AlertDto {
  return {
    key: 'sources_failed',
    kind: 'sources_failed',
    severity: 'warning',
    title: '2 sources could not be indexed',
    detail: 'The agent cannot answer from them. See why in Sources and try again.',
    count: 2,
    since: '2026-11-05T08:00:00Z',
    link: '/sources',
    ...overrides,
  };
}

export const waitingAlert = alertDto({
  key: 'visitors_waiting',
  kind: 'visitors_waiting',
  severity: 'critical',
  title: 'A visitor is waiting for a person',
  detail: 'They have waited 16 minutes. Join the conversation in the Inbox.',
  count: 1,
  link: '/inbox',
});

export function alertSettings(overrides: Partial<AlertSettingsDto> = {}): AlertSettingsDto {
  return {
    slack: false,
    email: false,
    kinds: [
      'visitors_waiting',
      'missed_requests',
      'model_missing',
      'provider_rejected',
      'sources_failed',
      'sources_waiting',
      'slack_failing',
      'email_failing',
      'rating_drop',
      'thumbs_drop',
      'unanswered_spike',
    ],
    waitingMinutes: 5,
    slackReady: true,
    emailReady: false,
    slackChannel: 'C0123456789',
    emailTo: null,
    ...overrides,
  };
}

/** GET /api/alerts from a getter, so a test can change the alerts between requests. */
export function alertsRoutes(get: () => AlertListDto = () => ({ alerts: [], snoozed: 0 })) {
  return {
    'GET /api/alerts': () => jsonResponse(get()),
  };
}
