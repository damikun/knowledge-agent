import { ClipboardPlus, MessagesSquare } from 'lucide-react';
import { Link } from 'react-router';
import { Badge, type BadgeTone } from '../../components/ui/Badge';
import { Button, buttonClass } from '../../components/ui/Button';
import { Table, TableCell, TableHead, TableHeader, TableRow, TableRowHeader } from '../../components/ui/Table';
import { formatDateTime, formatNumber, formatRelativeTime, pluralize } from '../../lib/format';
import { useReturnState } from '../../lib/returnTo';
import type { DatasetQuestionSource } from '../evals/AddToDatasetDialog';
import { onlyConversationPath } from '../inbox/inboxState';
import { widgetLabel } from '../inbox/status';
import { LabelChip } from '../labels/LabelChip';
import type { Analytics, AnalyticsDays, AnalyticsLabel, AnalyticsSegment, UnansweredQuestion, UnansweredReason } from './types';

const REASONS: Readonly<Record<UnansweredReason, { label: string; tone: BadgeTone; hint: string }>> = {
  handover: { label: 'Asked for a person', tone: 'warning', hint: 'The visitor or the assistant asked for a person.' },
  thumbs_down: { label: 'Rated not helpful', tone: 'danger', hint: 'The answer got a thumbs down.' },
  no_sources: { label: 'No sources found', tone: 'neutral', hint: 'The assistant searched your sources and found nothing to cite.' },
};

export function percent(part: number, whole: number): string {
  return whole > 0 ? `${Math.round((part / whole) * 100)}%` : '–';
}

interface QuestionListProps {
  questions: readonly UnansweredQuestion[];
  names: ReadonlyMap<string, string>;
  onAdd: (source: DatasetQuestionSource) => void;
}

export function QuestionList({ questions, names, onAdd }: QuestionListProps) {
  const returnState = useReturnState('Analytics');
  return (
    <ul aria-label="Questions the agent could not answer" className="divide-y divide-border-subtle">
      {questions.map((item) => (
        <li key={`${item.conversationId}-${item.question}`} className="flex flex-col gap-3 px-4 py-4 sm:flex-row sm:items-start">
          <div className="min-w-0 flex-1 space-y-1.5">
            <p className="text-sm font-medium break-words text-fg">{item.question}</p>
            <div className="flex flex-wrap items-center gap-1.5">
              {item.reasons.map((reason) => (
                <Badge key={reason} tone={REASONS[reason].tone} title={REASONS[reason].hint}>
                  {REASONS[reason].label}
                </Badge>
              ))}
              <span className="text-xs text-fg-muted">
                {item.count > 1 ? `Asked ${formatNumber(item.count)} times, last ` : 'Asked '}
                <time dateTime={item.lastAskedAt} title={formatDateTime(item.lastAskedAt)}>
                  {formatRelativeTime(item.lastAskedAt)}
                </time>
                {' · '}
                {item.widgetId ? widgetLabel(item.widgetId, names) : 'Portal chat'}
              </span>
            </div>
            {item.comment ? <p className="text-small break-words text-fg-secondary">Comment: “{item.comment}”</p> : null}
          </div>
          <div className="flex shrink-0 flex-wrap items-center gap-2">
            <Link to={onlyConversationPath(item.conversationId)} state={returnState} className={buttonClass({ variant: 'ghost', size: 'sm' })}>
              <MessagesSquare className="size-4" aria-hidden />
              Read
            </Link>
            <Button
              size="sm"
              icon={ClipboardPlus}
              onClick={() => onAdd({ question: item.question, answer: item.answer, conversationId: item.conversationId, messageId: item.messageId })}
            >
              Add to dataset
            </Button>
          </div>
        </li>
      ))}
    </ul>
  );
}

export function WidgetTable({ analytics, names }: { analytics: Analytics; names: ReadonlyMap<string, string> }) {
  return (
    <Table aria-label="Numbers per widget">
      <TableHead>
        <TableHeader>Widget</TableHeader>
        <TableHeader numeric>Conversations</TableHeader>
        <TableHeader numeric>Without a person</TableHeader>
        <TableHeader numeric>Handed to a person</TableHeader>
        <TableHeader numeric>Helpful answers</TableHeader>
        <TableHeader numeric>Chat rating</TableHeader>
      </TableHead>
      <tbody>
        {analytics.widgets.map((row) => (
          <TableRow key={row.widgetId ?? 'portal'}>
            <TableRowHeader>{row.widgetId ? widgetLabel(row.widgetId, names) : 'Portal chat'}</TableRowHeader>
            <TableCell numeric>{formatNumber(row.conversations)}</TableCell>
            <TableCell numeric>{percent(row.conversations - row.handedToPerson, row.conversations)}</TableCell>
            <TableCell numeric>{formatNumber(row.handedToPerson)}</TableCell>
            <TableCell numeric>{percent(row.thumbsUp, row.thumbsUp + row.thumbsDown)}</TableCell>
            <TableCell numeric>{row.averageRating === null ? '–' : row.averageRating.toFixed(1)}</TableCell>
          </TableRow>
        ))}
      </tbody>
    </Table>
  );
}

export function SegmentTable({ segments }: { segments: readonly AnalyticsSegment[] }) {
  const returnState = useReturnState('Analytics');
  return (
    <Table aria-label="Numbers per segment">
      <TableHead>
        <TableHeader>Segment</TableHeader>
        <TableHeader numeric>Active contacts</TableHeader>
        <TableHeader numeric>Conversations</TableHeader>
        <TableHeader numeric>Handover rate</TableHeader>
        <TableHeader numeric>Chat rating</TableHeader>
      </TableHead>
      <tbody>
        {segments.map((row) => (
          <TableRow key={row.segmentId}>
            <TableRowHeader>
              <Link to={`/contacts?segment=${encodeURIComponent(row.segmentId)}`} state={returnState} className="hover:underline">
                {row.name}
              </Link>
            </TableRowHeader>
            <TableCell numeric>{formatNumber(row.contacts)}</TableCell>
            <TableCell numeric>{formatNumber(row.conversations)}</TableCell>
            <TableCell numeric>{row.handoverRate === null ? '–' : `${Math.round(row.handoverRate * 100)}%`}</TableCell>
            <TableCell numeric>{row.averageRating === null ? '–' : row.averageRating.toFixed(1)}</TableCell>
          </TableRow>
        ))}
      </tbody>
    </Table>
  );
}

function change(label: AnalyticsLabel): { text: string; tone: string } {
  const difference = label.conversations - label.previousConversations;
  if (label.previousConversations === 0) {
    return { text: label.conversations > 0 ? 'New' : 'None before', tone: 'text-fg-muted' };
  }
  if (difference === 0) {
    return { text: 'Same as before', tone: 'text-fg-muted' };
  }
  return { text: `${difference > 0 ? '+' : '−'}${formatNumber(Math.abs(difference))} on the period before`, tone: 'text-fg-secondary' };
}

export function LabelList({ labels, days }: { labels: readonly AnalyticsLabel[]; days: AnalyticsDays }) {
  const returnState = useReturnState('Analytics');
  const most = Math.max(1, ...labels.map((label) => label.conversations));
  return (
    <ul aria-label="Conversations per label" className="divide-y divide-border-subtle">
      {labels.map((label) => {
        const delta = change(label);
        return (
          <li key={label.labelId}>
            <Link
              to={`/inbox?label=${encodeURIComponent(label.labelId.toLowerCase())}&period=${days}`} state={returnState}
              className="grid grid-cols-[minmax(0,10rem)_minmax(0,1fr)_auto] items-center gap-3 px-4 py-2.5 hover:bg-fill-hover sm:grid-cols-[minmax(0,12rem)_minmax(0,1fr)_auto_12rem]"
            >
              <LabelChip name={label.name} color={label.color} />
              <span aria-hidden className="h-2 overflow-hidden rounded-full bg-fill-muted">
                <span className="block h-full rounded-full bg-accent" style={{ width: `${(label.conversations / most) * 100}%` }} />
              </span>
              <span className="text-small font-medium text-fg tabular-nums">{pluralize(label.conversations, 'conversation')}</span>
              <span className={`col-span-3 text-xs sm:col-span-1 sm:text-right ${delta.tone}`}>{delta.text}</span>
            </Link>
          </li>
        );
      })}
    </ul>
  );
}
