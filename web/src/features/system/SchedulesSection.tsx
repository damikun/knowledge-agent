import { CardGrid } from '../../components/ui/Page';
import { useMutation } from '@tanstack/react-query';
import { CalendarClock, CircleCheck, Play } from 'lucide-react';
import { useId, type ReactNode } from 'react';
import { Badge } from '../../components/ui/Badge';
import { Button } from '../../components/ui/Button';
import { Card, EmptyState, ErrorMessage, Spinner } from '../../components/ui/feedback';
import { formatDateTime } from '../../lib/format';
import { jobsApi, useJobsOverview } from './api';
import { describeCron, jobKindDescription, jobKindLabel } from './jobs';
import type { JobScheduleDto } from './types';

function Fact({ label, children }: { label: string; children: ReactNode }) {
  return (
    <>
      <dt className="text-fg-muted">{label}</dt>
      <dd className="min-w-0 break-words text-fg">{children}</dd>
    </>
  );
}

function LocalTime({ value, fallback }: { value: string | null; fallback: string }) {
  return value ? <time dateTime={value}>{formatDateTime(value)}</time> : <span className="text-fg-muted">{fallback}</span>;
}

/** Common shapes in words, with the cron expression below as the exact rule. */
function Cron({ value }: { value: string }) {
  const described = describeCron(value);
  return (
    <>
      {described ? (
        <span className="block">
          {described.text}
          {described.atTime ? <span className="text-fg-muted"> UTC</span> : null}
        </span>
      ) : null}
      <span className="block text-xs text-fg-muted">
        <code className="font-mono">{value}</code>
        {described ? null : <span> UTC</span>}
      </span>
    </>
  );
}

function ScheduleCard({ schedule }: { schedule: JobScheduleDto }) {
  const headingId = useId();
  const run = useMutation({ mutationFn: () => jobsApi.runSchedule(schedule.name) });
  const description = jobKindDescription(schedule.kind);

  return (
    <li className="min-w-0">
      <section aria-labelledby={headingId} className="flex h-full flex-col rounded-2xl border border-border bg-surface p-4">
        <div className="flex min-w-0 items-center gap-3">
          <span aria-hidden className="flex size-10 shrink-0 items-center justify-center rounded-lg bg-fill-muted text-fg-muted">
            <CalendarClock className="size-5" />
          </span>
          <div className="min-w-0">
            <h3 id={headingId} className="truncate font-mono text-sm font-semibold text-fg" title={schedule.name}>
              {schedule.name}
            </h3>
            <div className="mt-0.5 flex min-w-0 items-center gap-2">
              <span className="truncate text-xs text-fg-muted">{jobKindLabel(schedule.kind)}</span>
              {schedule.enabled ? (
                <Badge tone="success" dot>
                  Enabled
                </Badge>
              ) : (
                <Badge>Disabled</Badge>
              )}
            </div>
          </div>
        </div>
        {description ? <p className="mt-3 text-small text-fg-secondary">{description}</p> : null}

        <dl className="mt-3 grid grid-cols-[auto_minmax(0,1fr)] gap-x-4 gap-y-2 text-small">
          <Fact label="Runs">
            <Cron value={schedule.cron} />
          </Fact>
          <Fact label="Next run">
            <LocalTime value={schedule.nextRunAt} fallback="Not scheduled" />
          </Fact>
        </dl>

        <div className="mt-auto flex flex-wrap items-center gap-x-3 gap-y-2 pt-4">
          <Button size="sm" icon={Play} loading={run.isPending} onClick={() => run.mutate()}>
            Run now
          </Button>
          <p role="status" className={run.isSuccess ? 'flex items-center gap-1.5 text-xs text-success-text' : 'sr-only'}>
            {run.isSuccess ? (
              <>
                <CircleCheck className="size-3.5 shrink-0" aria-hidden />
                Job queued.
              </>
            ) : null}
          </p>
        </div>
        {run.isError ? <ErrorMessage error={run.error} title="The job was not queued" className="mt-3" /> : null}
      </section>
    </li>
  );
}

export function SchedulesSection() {
  const overview = useJobsOverview();

  return (
    <section aria-labelledby="job-schedules-heading" className="space-y-3">
      <div>
        <h2 id="job-schedules-heading" className="text-section text-fg">
          Schedules
        </h2>
        <p className="mt-0.5 text-sm text-fg-secondary">
          Recurring jobs, set in configuration under <code className="font-mono text-xs">Jobs:Schedules</code>. Their times are in UTC; the next run
          is in your local time.
        </p>
      </div>
      {overview.isPending ? (
        <Spinner label="Loading schedules" />
      ) : overview.isError ? (
        <ErrorMessage error={overview.error} title="Could not load schedules" />
      ) : overview.data.schedules.length === 0 ? (
        <Card>
          <EmptyState icon={CalendarClock} title="No schedules" description="The API reported no recurring jobs." />
        </Card>
      ) : (
        <CardGrid label="Schedules">
          {overview.data.schedules.map((schedule) => (
            <ScheduleCard key={schedule.name} schedule={schedule} />
          ))}
        </CardGrid>
      )}
    </section>
  );
}
