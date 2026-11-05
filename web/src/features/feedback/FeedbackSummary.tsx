import { useId, type ReactNode } from 'react';
import { EntityAvatar } from '../../components/ui/EntityCard';
import { Card } from '../../components/ui/feedback';
import { StatCard } from '../../components/ui/StatCard';
import { formatNumber } from '../../lib/format';
import type { FeedbackSummaryDto } from './types';

export interface Names {
  widget: (id: string | null) => string;
  agent: (id: string | null) => string | null;
}

interface Totals {
  answers: number;
  positive: number;
  negative: number;
  ratings: number;
  /** Sum of stars, so averages combine weighted by the number of ratings. */
  stars: number;
  agentRecorded: number;
}

function totalsOf(rows: readonly FeedbackSummaryDto[]): Totals {
  return rows.reduce<Totals>(
    (sum, row) => ({
      answers: sum.answers + row.answers,
      positive: sum.positive + row.positive,
      negative: sum.negative + row.negative,
      ratings: sum.ratings + row.ratings,
      stars: sum.stars + (row.averageRating ?? 0) * row.ratings,
      agentRecorded: sum.agentRecorded + row.agentRecorded,
    }),
    { answers: 0, positive: 0, negative: 0, ratings: 0, stars: 0, agentRecorded: 0 },
  );
}

function summaryText(totals: Totals) {
  return {
    helpful: totals.answers === 0 ? '–' : `${Math.round((totals.positive / totals.answers) * 100)}%`,
    helpfulDetail: totals.answers === 0 ? 'No answer ratings' : `${formatNumber(totals.positive)} up, ${formatNumber(totals.negative)} down`,
    rating: totals.ratings === 0 ? '–' : `${(totals.stars / totals.ratings).toFixed(1)}/5`,
    ratingDetail: totals.ratings === 0 ? 'No chat ratings' : `From ${formatNumber(totals.ratings)} ${totals.ratings === 1 ? 'rating' : 'ratings'}`,
    notes: formatNumber(totals.agentRecorded),
    notesDetail: totals.agentRecorded === 1 ? 'note' : 'notes',
  };
}

function Stat({ label, value, detail }: { label: string; value: ReactNode; detail: ReactNode }) {
  return (
    <div className="min-w-0">
      <dt className="truncate text-xs text-fg-muted">{label}</dt>
      <dd className="mt-0.5">
        <span className="text-section text-fg tabular-nums">{value}</span>
        <span className="mt-0.5 block text-xs text-fg-muted">{detail}</span>
      </dd>
    </div>
  );
}

function SummaryCard({ summary, names }: { summary: FeedbackSummaryDto; names: Names }) {
  const headingId = useId();
  const widget = names.widget(summary.widgetId);
  const agent = names.agent(summary.agentId);
  const text = summaryText(totalsOf([summary]));
  return (
    <Card>
      <section aria-labelledby={headingId} className="space-y-4 p-4">
        <div className="flex min-w-0 items-center gap-3">
          <EntityAvatar name={widget} />
          <div className="min-w-0">
            <h3 id={headingId} className="truncate text-sm font-semibold text-fg">
              {widget}
            </h3>
            {agent ? <p className="truncate text-small text-fg-secondary">{agent}</p> : null}
          </div>
        </div>
        <dl className="grid grid-cols-3 gap-3">
          <Stat label="Helpful answers" value={text.helpful} detail={text.helpfulDetail} />
          <Stat label="Chat rating" value={text.rating} detail={text.ratingDetail} />
          <Stat label="Noted by the assistant" value={text.notes} detail={text.notesDetail} />
        </dl>
      </section>
    </Card>
  );
}

export function Summary({ rows, names }: { rows: readonly FeedbackSummaryDto[]; names: Names }) {
  const text = summaryText(totalsOf(rows));
  return (
    <>
      <div className="grid gap-3 sm:grid-cols-3">
        <StatCard label="Helpful answers" value={text.helpful} caption={text.helpfulDetail} />
        <StatCard label="Chat rating" value={text.rating} caption={text.ratingDetail} />
        <StatCard label="Noted by the assistant" value={text.notes} caption={text.notesDetail} />
      </div>
      {rows.length > 1 ? (
        <section aria-labelledby="feedback-breakdown-heading" className="space-y-3">
          <h2 id="feedback-breakdown-heading" className="text-section text-fg">
            By widget and agent
          </h2>
          <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
            {rows.map((row) => (
              <SummaryCard key={`${row.widgetId ?? 'portal'}-${row.agentId ?? 'built-in'}`} summary={row} names={names} />
            ))}
          </div>
        </section>
      ) : null}
    </>
  );
}
