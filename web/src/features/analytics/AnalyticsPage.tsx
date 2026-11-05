import { CircleCheck, Library, MessagesSquare, Tags } from 'lucide-react';
import { useId, useMemo, useState, type ReactNode } from 'react';
import { Link, useSearchParams } from 'react-router';
import { Button, buttonClass } from '../../components/ui/Button';
import { Card, EmptyState, ErrorMessage, PageHeader } from '../../components/ui/feedback';
import { SegmentedControl, type SegmentedOption } from '../../components/ui/SegmentedControl';
import { Skeleton } from '../../components/ui/Skeleton';
import { StatCard } from '../../components/ui/StatCard';
import { Page } from '../../components/ui/Page';
import { formatNumber, pluralize } from '../../lib/format';
import { AddToDatasetDialog, type DatasetQuestionSource } from '../evals/AddToDatasetDialog';
import { labelsAvailable, useLabels } from '../labels/api';
import { ManageLabelsSheet } from '../labels/ManageLabelsSheet';
import { useWidgets } from '../widgets/api';
import { useAnalytics } from './api';
import { LabelList, percent, QuestionList, SegmentTable, WidgetTable } from './AnalyticsSections';
import { DailyChart } from './DailyChart';
import type { Analytics, AnalyticsDays } from './types';
import { useKeptReturnState, useReturnState } from '../../lib/returnTo';

type Period = `${AnalyticsDays}`;

const PERIODS: readonly SegmentedOption<Period>[] = [
  { value: '7', label: '7 days' },
  { value: '30', label: '30 days' },
  { value: '90', label: '90 days' },
];

function dailyAverage(conversations: number, days: number): string {
  const average = conversations / Math.max(1, days);
  if (conversations === 0) {
    return 'None in this period';
  }
  return average < 1 ? 'Less than 1 a day' : `About ${formatNumber(Math.round(average))} a day`;
}

function Section({ title, description, action, children }: { title: string; description?: ReactNode; action?: ReactNode; children: ReactNode }) {
  const id = useId();
  return (
    <section aria-labelledby={id} className="space-y-3">
      <div className="flex flex-wrap items-end justify-between gap-x-4 gap-y-2">
        <div>
          <h2 id={id} className="text-section text-fg">
            {title}
          </h2>
          {description ? <p className="mt-0.5 max-w-160 text-sm text-fg-secondary">{description}</p> : null}
        </div>
        {action}
      </div>
      {children}
    </section>
  );
}

function Stats({ analytics }: { analytics: Analytics }) {
  const { totals } = analytics;
  const rated = totals.thumbsUp + totals.thumbsDown;
  return (
    <ul aria-label="Key numbers" className="grid grid-cols-[repeat(auto-fit,minmax(10.5rem,1fr))] gap-3">
      <li>
        <StatCard
          label="Conversations"
          value={formatNumber(totals.conversations)}
          caption={dailyAverage(totals.conversations, analytics.days)}
          className="h-full"
        />
      </li>
      <li>
        <StatCard
          label="Answered without a person"
          value={percent(totals.answeredWithoutPerson, totals.conversations)}
          caption={totals.conversations > 0 ? `${formatNumber(totals.answeredWithoutPerson)} of ${formatNumber(totals.conversations)}` : 'No conversations yet'}
          className="h-full"
        />
      </li>
      <li>
        <StatCard
          label="Handed to a person"
          value={percent(totals.handedToPerson, totals.conversations)}
          caption={pluralize(totals.handedToPerson, 'conversation')}
          className="h-full"
        />
      </li>
      <li>
        <StatCard
          label="Helpful answers"
          value={percent(totals.thumbsUp, rated)}
          caption={rated > 0 ? `${formatNumber(totals.thumbsUp)} up, ${formatNumber(totals.thumbsDown)} down` : 'No answer ratings'}
          className="h-full"
        />
      </li>
      <li>
        <StatCard
          label="Chat rating"
          value={totals.averageRating === null ? '–' : `${totals.averageRating.toFixed(1)}/5`}
          caption={totals.ratings > 0 ? `From ${pluralize(totals.ratings, 'rating')}` : 'No chat ratings'}
          className="h-full"
        />
      </li>
    </ul>
  );
}

function LoadingCards() {
  return (
    <div className="grid grid-cols-[repeat(auto-fit,minmax(10.5rem,1fr))] gap-3">
      <p role="status" className="sr-only">
        Loading the numbers
      </p>
      {Array.from({ length: 5 }, (_, index) => (
        <div key={index} aria-hidden className="rounded-2xl border border-border bg-surface p-4">
          <Skeleton className="h-4 w-24" />
          <Skeleton className="mt-2 h-7 w-14" />
          <Skeleton className="mt-2 h-3.5 w-28" />
        </div>
      ))}
    </div>
  );
}

export function AnalyticsPage() {
  const returnState = useReturnState('Analytics');
  const [params, setParams] = useSearchParams();
  const requested = Number(params.get('days'));
  const days: AnalyticsDays = requested === 7 || requested === 90 ? requested : 30;
  const kept = useKeptReturnState();
  const analytics = useAnalytics(days);
  const hasLabels = labelsAvailable(useLabels().data);
  const widgets = useWidgets();
  const [adding, setAdding] = useState<DatasetQuestionSource | null>(null);
  const [managingLabels, setManagingLabels] = useState(false);

  const names = useMemo(() => new Map((widgets.data ?? []).map((widget) => [widget.id.toLowerCase(), widget.name] as const)), [widgets.data]);
  const data = analytics.data;
  const dates = data?.daily.map((day) => day.date) ?? [];

  return (
    <Page>
      <PageHeader
        title="Analytics"
        description="Whether the agent helps your visitors, and the questions it could not answer, so you know which sources to add and which answers to check."
        actions={
          <SegmentedControl
            label="Period"
            options={PERIODS}
            value={`${days}` as Period}
            onChange={(value) => setParams(value === '30' ? {} : { days: value }, { replace: true, state: kept })}
          />
        }
      />

      {analytics.isError ? (
        <ErrorMessage error={analytics.error} title="Could not load the analytics" />
      ) : !data ? (
        <LoadingCards />
      ) : (
        <>
          <Stats analytics={data} />

          <div className="grid gap-8">
            <Section
              title="Questions the agent could not answer"
              description="Asked for a person, rated down, or no source found. Most asked first: add a source that answers them, or add them to a dataset."
              action={
                <Link to="/sources" state={returnState} className={buttonClass({ variant: 'ghost', size: 'sm' })}>
                  <Library className="size-4" aria-hidden />
                  Open sources
                </Link>
              }
            >
              <Card className="overflow-hidden">
                {data.unanswered.length === 0 ? (
                  <EmptyState
                    icon={CircleCheck}
                    title="Nothing to fix in this period"
                    description="No one asked for a person, rated an answer down, or got an answer without sources."
                  />
                ) : (
                  <QuestionList questions={data.unanswered} names={names} onAdd={setAdding} />
                )}
              </Card>
            </Section>

            <div className="grid gap-8 lg:grid-cols-2">
              <Section title="Conversations per day" description="By the day they started, in UTC.">
                <Card className="p-4">
                  <DailyChart
                    label="Conversations per day"
                    dates={dates}
                    mode="stacked"
                    emptyLabel="No conversations in this period"
                    summary={`${pluralize(data.totals.conversations, 'conversation')} in ${data.days} days, ${formatNumber(data.totals.handedToPerson)} handed to a person.`}
                    series={[
                      {
                        key: 'ai',
                        label: 'Answered without a person',
                        fill: 'bg-accent',
                        values: data.daily.map((day) => day.conversations - day.handedToPerson),
                      },
                      { key: 'person', label: 'Handed to a person', fill: 'bg-border-strong', values: data.daily.map((day) => day.handedToPerson) },
                    ]}
                  />
                </Card>
              </Section>
              <Section title="Answer ratings per day" description="Thumbs up above the line, thumbs down below it.">
                <Card className="p-4">
                  <DailyChart
                    label="Answer ratings per day"
                    dates={dates}
                    mode="diverging"
                    emptyLabel="No answers were rated in this period"
                    summary={`${formatNumber(data.totals.thumbsUp)} thumbs up and ${formatNumber(data.totals.thumbsDown)} thumbs down in ${data.days} days.`}
                    series={[
                      { key: 'up', label: 'Helpful', fill: 'bg-success', values: data.daily.map((day) => day.thumbsUp) },
                      { key: 'down', label: 'Not helpful', fill: 'bg-danger', values: data.daily.map((day) => day.thumbsDown) },
                    ]}
                    details={(index) => {
                      const day = data.daily[index];
                      return day && day.ratings > 0
                        ? `Chat rating ${day.averageRating?.toFixed(1)} from ${pluralize(day.ratings, 'rating')}`
                        : 'No chat ratings';
                    }}
                  />
                </Card>
              </Section>
            </div>
          </div>

          <div className="grid items-start gap-8 2xl:grid-cols-2">
            {hasLabels || (data.labels?.length ?? 0) > 0 ? (
              <Section
                title="Conversations per label"
                description="Conversations per label, compared with the period before. Open one to see them."
              >
                <Card className="overflow-hidden">
                  {data.labels.length === 0 ? (
                    <EmptyState
                      icon={Tags}
                      title="No labeled conversations in this period"
                      description="Labels are added automatically once a model is connected, and staff can add them in the Inbox."
                      action={
                        <Button size="sm" icon={Tags} onClick={() => setManagingLabels(true)}>
                          Manage labels
                        </Button>
                      }
                    />
                  ) : (
                    <LabelList labels={data.labels} days={days} />
                  )}
                </Card>
              </Section>
            ) : null}

            {data.segments.length > 0 ? (
              <Section title="By segment" description="Conversations in this period with the people who are in each segment now.">
                <Card className="overflow-hidden">
                  <SegmentTable segments={data.segments} />
                </Card>
              </Section>
            ) : null}

            <Section title="By widget" description="The portal chat and each website widget in this period.">
              <Card className="overflow-hidden">
                {data.widgets.length === 0 ? (
                  <EmptyState icon={MessagesSquare} title="No conversations in this period" description="Numbers appear once people chat with the agent." />
                ) : (
                  <WidgetTable analytics={data} names={names} />
                )}
              </Card>
            </Section>
          </div>
        </>
      )}

      {adding ? <AddToDatasetDialog source={adding} onClose={() => setAdding(null)} /> : null}
      {managingLabels ? <ManageLabelsSheet open onClose={() => setManagingLabels(false)} /> : null}
    </Page>
  );
}
