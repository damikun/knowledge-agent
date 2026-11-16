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

export interface AlertListDto {
  alerts: AlertDto[];
  snoozed: number;
}

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
