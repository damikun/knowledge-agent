import type { IsoDateTime } from '../../lib/types';

export type AlertSeverity = 'info' | 'warning' | 'critical';

export type AlertKind =
  | 'visitors_waiting'
  | 'missed_requests'
  | 'model_missing'
  | 'provider_rejected'
  | 'sources_failed'
  | 'sources_waiting'
  | 'slack_failing'
  | 'email_failing'
  | 'rating_drop'
  | 'thumbs_drop'
  | 'unanswered_spike';

/** `link` is the portal path where it gets fixed. */
export interface AlertDto {
  key: string;
  kind: AlertKind;
  severity: AlertSeverity;
  title: string;
  detail: string;
  count: number;
  since: IsoDateTime;
  link: string;
}

/** GET /api/alerts: most severe first; `snoozed` counts the ones an admin hid. */
export interface AlertListDto {
  alerts: AlertDto[];
  snoozed: number;
}

/** GET /api/alerts/settings: where new alerts are sent. The ready flags say whether that integration can send at all. */
export interface AlertSettingsDto {
  slack: boolean;
  email: boolean;
  kinds: AlertKind[];
  waitingMinutes: number;
  slackReady: boolean;
  emailReady: boolean;
  slackChannel: string | null;
  emailTo: string | null;
}

export interface SaveAlertSettingsRequest {
  slack: boolean;
  email: boolean;
  kinds: AlertKind[];
  waitingMinutes: number;
}
