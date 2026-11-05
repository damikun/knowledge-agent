import { BellRing, Check, Loader2, MessageSquare, Mail } from 'lucide-react';
import { useId, useState, type ReactNode } from 'react';
import { Badge } from '../../components/ui/Badge';
import { Button } from '../../components/ui/Button';
import { ErrorMessage } from '../../components/ui/feedback';
import { Select, type SelectOption } from '../../components/ui/Select';
import { Skeleton } from '../../components/ui/Skeleton';
import { Switch } from '../../components/ui/Switch';
import { ALERT_KINDS, useAlertSettings, useSaveAlertSettings, type AlertKind, type AlertSettingsDto } from '../alerts';
import type { IntegrationStatus } from './IntegrationParts';

const WAITING_CHOICES = [1, 2, 5, 10, 15, 30, 60, 120, 240];

const ALERT_GROUPS: ReadonlyArray<{ title: string; kinds: readonly AlertKind[] }> = [
  { title: 'Conversations', kinds: ['visitors_waiting', 'missed_requests', 'unanswered_spike'] },
  { title: 'Answer quality', kinds: ['rating_drop', 'thumbs_drop'] },
  { title: 'Models and sources', kinds: ['model_missing', 'provider_rejected', 'sources_failed', 'sources_waiting'] },
  { title: 'Slack and email', kinds: ['slack_failing', 'email_failing'] },
];

interface AlertChoices {
  slack: boolean;
  email: boolean;
  kinds: ReadonlySet<AlertKind>;
  waitingMinutes: number;
}

function waitingLabel(minutes: number): string {
  if (minutes % 60 === 0) {
    const hours = minutes / 60;
    return hours === 1 ? '1 hour' : `${hours} hours`;
  }
  return minutes === 1 ? '1 minute' : `${minutes} minutes`;
}

function waitingOptions(current: number): SelectOption<string>[] {
  const minutes = WAITING_CHOICES.includes(current) ? WAITING_CHOICES : [...WAITING_CHOICES, current].sort((a, b) => a - b);
  return minutes.map((value) => ({ value: String(value), label: waitingLabel(value) }));
}

function alertStatus(settings: AlertSettingsDto): IntegrationStatus {
  const slack = settings.slack && settings.slackReady;
  const email = settings.email && settings.emailReady;
  if (!settings.slack && !settings.email) {
    return { tone: 'neutral', label: 'Off' };
  }
  if (!slack && !email) {
    return { tone: 'warning', label: 'Nowhere to send' };
  }
  return { tone: 'success', label: slack && email ? 'Slack and email' : slack ? 'Slack' : 'Email' };
}

/** Mounted once settings load, so the choices start from what is saved. */
function AlertSettingsForm({ settings }: { settings: AlertSettingsDto }) {
  const prefix = useId();
  const headingId = `${prefix}-heading`;
  const waitingId = `${prefix}-waiting`;
  const [choices, setChoices] = useState<AlertChoices>(() => ({
    slack: settings.slack,
    email: settings.email,
    kinds: new Set(settings.kinds),
    waitingMinutes: settings.waitingMinutes,
  }));
  const save = useSaveAlertSettings();
  const send = ({ slack, email, kinds, waitingMinutes }: AlertChoices) =>
    save.mutate({ slack, email, waitingMinutes, kinds: ALERT_KINDS.map((item) => item.kind).filter((kind) => kinds.has(kind)) });

  // Saves queue up, so the last change wins.
  const change = (update: Partial<AlertChoices>) => {
    const next = { ...choices, ...update };
    setChoices(next);
    send(next);
  };

  const toggle = (kind: AlertKind, checked: boolean) => {
    const kinds = new Set(choices.kinds);
    if (checked) {
      kinds.add(kind);
    } else {
      kinds.delete(kind);
    }
    change({ kinds });
  };

  const status = alertStatus({ ...settings, slack: choices.slack, email: choices.email });
  const byKind = new Map(ALERT_KINDS.map((item) => [item.kind, item]));

  return (
    <section aria-labelledby={headingId} className="flex flex-col rounded-2xl border border-border bg-surface lg:col-span-2">
      <div className="flex items-start gap-3 p-5">
        <span className="flex size-10 shrink-0 items-center justify-center rounded-lg bg-fill-muted text-fg-secondary">
          <BellRing className="size-5" aria-hidden />
        </span>
        <div className="min-w-0 flex-1">
          <div className="flex flex-wrap items-center justify-between gap-x-3 gap-y-1">
            <h3 id={headingId} className="text-section text-fg">
              Alerts
            </h3>
            <div className="flex items-center gap-3">
              <SaveState pending={save.isPending} saved={save.isSuccess} />
              <Badge tone={status.tone} dot>
                {status.label}
              </Badge>
            </div>
          </div>
          <p className="mt-1 max-w-3xl text-sm text-fg-secondary">
            Tells your team when something needs attention. Each alert is sent once, again only after it was resolved and came back, and at most
            one of a kind per hour. Changes are saved as you make them.
          </p>
        </div>
      </div>

      {save.isError ? (
        <div className="flex flex-wrap items-start gap-3 px-5 pb-4">
          <ErrorMessage error={save.error} title="Your last change was not saved" className="min-w-0 flex-1" />
          <Button variant="secondary" size="sm" onClick={() => send(choices)}>
            Try again
          </Button>
        </div>
      ) : null}

      <div className="space-y-6 border-t border-border-subtle px-5 py-5">
        <div>
          <h4 className="text-small font-medium text-fg">Where to send them</h4>
          <div className="mt-3 grid gap-3 md:grid-cols-3">
            <Channel
              icon={MessageSquare}
              hintId={`${prefix}-slack-hint`}
              hint={settings.slackReady && settings.slackChannel ? `To ${settings.slackChannel}, the channel for requests.` : 'Set up Slack above first.'}
            >
              <Switch checked={choices.slack} onChange={(slack) => change({ slack })} label="Post to Slack" describedBy={`${prefix}-slack-hint`} />
            </Channel>
            <Channel
              icon={Mail}
              hintId={`${prefix}-email-hint`}
              hint={settings.emailReady && settings.emailTo ? `To ${settings.emailTo}, the support address.` : 'Set up email with a support address above first.'}
            >
              <Switch checked={choices.email} onChange={(email) => change({ email })} label="Send an email" describedBy={`${prefix}-email-hint`} />
            </Channel>
            <div className="rounded-xl border border-border-subtle p-3">
              <label htmlFor={waitingId} className="block text-sm text-fg">
                A visitor waiting longer than
              </label>
              <Select
                id={waitingId}
                aria-describedby={`${waitingId}-hint`}
                value={String(choices.waitingMinutes)}
                options={waitingOptions(choices.waitingMinutes)}
                onChange={(value) => change({ waitingMinutes: Number(value) })}
                className="mt-2 w-40"
              />
              <p id={`${waitingId}-hint`} className="mt-1.5 text-xs text-fg-muted">
                Also shown on Home and in the bell.
              </p>
            </div>
          </div>
        </div>

        <fieldset>
          <legend className="text-small font-medium text-fg">Which alerts</legend>
          <div className="mt-3 grid gap-3 md:grid-cols-2">
            {ALERT_GROUPS.map((group) => (
              <div key={group.title} className="rounded-xl border border-border-subtle">
                <div className="border-b border-border-subtle px-3 py-2 text-xs font-medium text-fg-muted">{group.title}</div>
                <ul className="divide-y divide-border-subtle">
                  {group.kinds.map((kind) => {
                    const item = byKind.get(kind)!;
                    const hintId = `${prefix}-${kind}`;
                    return (
                      <li key={kind} className="px-3 py-2.5">
                        <Switch checked={choices.kinds.has(kind)} onChange={(checked) => toggle(kind, checked)} label={item.label} describedBy={hintId} />
                        <p id={hintId} className="mt-0.5 pl-10 text-xs text-fg-muted">
                          {item.description}
                        </p>
                      </li>
                    );
                  })}
                </ul>
              </div>
            ))}
          </div>
        </fieldset>
      </div>
    </section>
  );
}

function Channel({ icon: Icon, hint, hintId, children }: { icon: typeof Mail; hint: string; hintId: string; children: ReactNode }) {
  return (
    <div className="flex items-start gap-3 rounded-xl border border-border-subtle p-3">
      <div className="min-w-0 flex-1 space-y-1">
        {children}
        <p id={hintId} className="pl-10 text-xs text-fg-muted">
          {hint}
        </p>
      </div>
      <Icon className="mt-0.5 size-4 shrink-0 text-fg-muted" aria-hidden />
    </div>
  );
}

function SaveState({ pending, saved }: { pending: boolean; saved: boolean }) {
  return (
    <span role="status" className="flex items-center gap-1 text-xs text-fg-muted">
      {pending ? (
        <>
          <Loader2 className="size-3.5 animate-spin" aria-hidden />
          Saving
        </>
      ) : saved ? (
        <>
          <Check className="size-3.5 text-success-text" aria-hidden />
          Saved
        </>
      ) : null}
    </span>
  );
}

export function AlertSettingsCard() {
  const settings = useAlertSettings();
  if (settings.isPending) {
    return (
      <div className="lg:col-span-2">
        <Skeleton shape="card" className="h-64" />
      </div>
    );
  }
  if (settings.isError) {
    return (
      <div className="lg:col-span-2">
        <ErrorMessage error={settings.error} title="Could not load the alert settings" />
      </div>
    );
  }
  return <AlertSettingsForm settings={settings.data} />;
}
