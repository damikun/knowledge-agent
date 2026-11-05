import type { Tone } from '../../components/ui/tone';
import type { AlertKind, AlertSeverity } from './types';

export const ALERT_KINDS: ReadonlyArray<{ kind: AlertKind; label: string; description: string }> = [
  { kind: 'visitors_waiting', label: 'Visitors waiting', description: 'Someone waits for a person longer than you set.' },
  { kind: 'missed_requests', label: 'Missed requests', description: 'Nobody joined in time and nobody replied since.' },
  { kind: 'model_missing', label: 'No model connected', description: 'The agent has no chat or embedding model.' },
  { kind: 'provider_rejected', label: 'API key rejected', description: 'A model provider refused its key.' },
  { kind: 'sources_failed', label: 'Sources failed', description: 'A source could not be indexed.' },
  { kind: 'sources_waiting', label: 'Sources waiting', description: 'Sources wait for an embedding model.' },
  { kind: 'slack_failing', label: 'Slack problems', description: 'Slack is not connected or a request could not be posted.' },
  { kind: 'email_failing', label: 'Email problems', description: 'A request could not be emailed.' },
  { kind: 'rating_drop', label: 'Ratings dropped', description: 'Ratings of the last day are well below the week before.' },
  { kind: 'thumbs_drop', label: 'Fewer thumbs up', description: 'Fewer answers got a thumbs up than the week before.' },
  { kind: 'unanswered_spike', label: 'Unanswered questions', description: 'More questions went unanswered than usual.' },
];

export const ALL_ALERT_KINDS: readonly AlertKind[] = ALERT_KINDS.map((item) => item.kind);

export const SEVERITY_TONES: Readonly<Record<AlertSeverity, Tone>> = {
  critical: 'danger',
  warning: 'warning',
  info: 'info',
};

export const SEVERITY_LABELS: Readonly<Record<AlertSeverity, string>> = {
  critical: 'Urgent',
  warning: 'Needs attention',
  info: 'For your information',
};

/** Hours, or null to hide it until it changes. */
export const SNOOZE_CHOICES: ReadonlyArray<{ hours: number | null; label: string; done: string }> = [
  { hours: 1, label: 'For 1 hour', done: 'Snoozed for 1 hour' },
  { hours: 24, label: 'For 1 day', done: 'Snoozed for 1 day' },
  { hours: 168, label: 'For 1 week', done: 'Snoozed for 1 week' },
  { hours: null, label: 'Until it changes', done: 'Hidden until it changes' },
];
