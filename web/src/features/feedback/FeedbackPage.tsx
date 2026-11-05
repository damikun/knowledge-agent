import { FilterSelect } from '../../components/ui/FilterSelect';
import { MessageSquareText } from 'lucide-react';
import { useMemo, useState } from 'react';
import { useSearchParams } from 'react-router';
import { Card, EmptyState, ErrorMessage, PageHeader, Spinner } from '../../components/ui/feedback';
import { SegmentedControl, type SegmentedOption } from '../../components/ui/SegmentedControl';
import { Page } from '../../components/ui/Page';
import { shortId } from '../../lib/format';
import { useAgents } from '../agents/api';
import { isAdmin, useMe } from '../auth/api';
import { AddToDatasetDialog, type DatasetQuestionSource } from '../evals/AddToDatasetDialog';
import { useWidgets } from '../widgets/api';
import { useFeedback, useFeedbackSummary } from './api';
import type { FeedbackDays, FeedbackFilters, FeedbackKind } from './types';
import { useKeptReturnState } from '../../lib/returnTo';
import { FeedbackItem } from './FeedbackItem';
import { Summary, type Names } from './FeedbackSummary';

type Period = `${FeedbackDays}`;

const PERIODS: readonly SegmentedOption<Period>[] = [
  { value: '7', label: '7 days' },
  { value: '30', label: '30 days' },
  { value: '90', label: '90 days' },
];

const KIND_FILTERS: ReadonlyArray<[FeedbackKind | '', string]> = [
  ['', 'All kinds'],
  ['answer', 'Answer ratings'],
  ['conversation', 'Chat ratings'],
  ['agent', 'Noted by the assistant'],
];

const RATING_FILTERS: ReadonlyArray<[FeedbackFilters['positive'], string]> = [
  ['', 'All ratings'],
  ['true', 'Positive'],
  ['false', 'Negative'],
];

function readFilters(params: URLSearchParams): FeedbackFilters {
  const days = Number(params.get('days'));
  const kind = params.get('kind');
  const positive = params.get('rating');
  return {
    days: days === 7 || days === 90 ? days : 30,
    widgetId: params.get('widget')?.toLowerCase() ?? '',
    agentId: params.get('agent')?.toLowerCase() ?? '',
    kind: kind === 'answer' || kind === 'conversation' || kind === 'agent' ? kind : '',
    positive: positive === 'true' || positive === 'false' ? positive : '',
  };
}

function toSearch(filters: FeedbackFilters): URLSearchParams {
  const search = new URLSearchParams();
  if (filters.days !== 30) {
    search.set('days', String(filters.days));
  }
  const optional: Array<[string, string]> = [
    ['widget', filters.widgetId],
    ['agent', filters.agentId],
    ['kind', filters.kind],
    ['rating', filters.positive],
  ];
  for (const [key, value] of optional) {
    if (value) {
      search.set(key, value);
    }
  }
  return search;
}

export function FeedbackPage() {
  const [params, setParams] = useSearchParams();
  const filters = readFilters(params);
  const kept = useKeptReturnState();
  const admin = isAdmin(useMe().data);
  // Support users can't read widgets (403), so they see widget ids.
  const widgets = useWidgets(admin);
  const agents = useAgents();
  const summary = useFeedbackSummary(filters.days);
  const feedback = useFeedback(filters);
  const [adding, setAdding] = useState<DatasetQuestionSource | null>(null);

  const names = useMemo<Names>(() => {
    const widgetNames = new Map((widgets.data ?? []).map((widget) => [widget.id.toLowerCase(), widget.name] as const));
    const agentNames = new Map((agents.data ?? []).map((agent) => [agent.id.toLowerCase(), agent.name] as const));
    return {
      widget: (id) => (id ? (widgetNames.get(id.toLowerCase()) ?? `Widget ${shortId(id)}`) : 'Portal chat'),
      agent: (id) => (id ? (agentNames.get(id.toLowerCase()) ?? null) : (agents.data?.find((agent) => agent.isBuiltIn)?.name ?? null)),
    };
  }, [widgets.data, agents.data]);

  const setFilters = (patch: Partial<FeedbackFilters>) => setParams(toSearch({ ...filters, ...patch }), { replace: true, state: kept });

  const widgetIds = admin
    ? (widgets.data ?? []).map((widget) => widget.id.toLowerCase())
    : [...new Set([...(summary.data ?? []).flatMap((row) => (row.widgetId ? [row.widgetId.toLowerCase()] : [])), ...(filters.widgetId ? [filters.widgetId] : [])])];
  const widgetChoices: Array<readonly [string, string]> = [
    ['', 'All widgets'],
    ...widgetIds.map((id) => [id, names.widget(id)] as const).sort((a, b) => a[1].localeCompare(b[1])),
  ];
  const agentChoices: Array<readonly [string, string]> = [
    ['', 'All agents'],
    ...(agents.data ?? []).map((agent) => [agent.id.toLowerCase(), agent.name] as const),
    ...(filters.agentId && !agents.data?.some((agent) => agent.id.toLowerCase() === filters.agentId) ? [[filters.agentId, 'Unknown agent'] as const] : []),
  ];
  const filtered = Boolean(filters.widgetId || filters.agentId || filters.kind || filters.positive);
  // The API stores the built-in agent's feedback without an agent, so match the agent id exactly.
  const summaryRows = (summary.data ?? []).filter(
    (row) =>
      (!filters.widgetId || row.widgetId?.toLowerCase() === filters.widgetId) && (!filters.agentId || row.agentId?.toLowerCase() === filters.agentId),
  );

  return (
    <Page>
      <PageHeader title="Feedback" description="How visitors and staff rate the answers and chats, and what people told the assistant." />

      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex w-full flex-wrap gap-2 sm:w-auto">
          <FilterSelect label="Widget" value={filters.widgetId} choices={widgetChoices} onChange={(widgetId) => setFilters({ widgetId })} />
          <FilterSelect label="Agent" value={filters.agentId} choices={agentChoices} onChange={(agentId) => setFilters({ agentId })} />
        </div>
        <SegmentedControl
          label="Period"
          options={PERIODS}
          value={`${filters.days}` as const}
          onChange={(value) => setFilters({ days: Number(value) as FeedbackDays })}
        />
      </div>

      {summary.isPending ? (
        <Spinner label="Loading the summary" />
      ) : summary.isError ? (
        <ErrorMessage error={summary.error} title="Could not load the summary" />
      ) : (
        <Summary rows={summaryRows} names={names} />
      )}

      <section aria-labelledby="feedback-list-heading" className="space-y-3">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <h2 id="feedback-list-heading" className="text-section text-fg">
            Latest feedback
          </h2>
          <div className="flex w-full flex-wrap gap-2 sm:w-auto">
            <FilterSelect label="Kind" value={filters.kind} choices={KIND_FILTERS} onChange={(kind) => setFilters({ kind })} />
            <FilterSelect label="Rating" value={filters.positive} choices={RATING_FILTERS} onChange={(positive) => setFilters({ positive })} />
          </div>
        </div>
        <Card>
          {feedback.isPending ? (
            <div className="p-4">
              <Spinner label="Loading feedback" />
            </div>
          ) : feedback.isError ? (
            <div className="p-4">
              <ErrorMessage error={feedback.error} title="Could not load the feedback" />
            </div>
          ) : feedback.data.length === 0 ? (
            <EmptyState
              icon={MessageSquareText}
              title="No feedback"
              description={filtered ? 'Nothing matches the filters.' : 'Ratings from the chat and the website widgets appear here.'}
            />
          ) : (
            <ul className="divide-y divide-border-subtle" aria-label="Feedback">
              {feedback.data.map((item) => (
                <FeedbackItem key={item.id} feedback={item} names={names} onAdd={admin ? setAdding : null} />
              ))}
            </ul>
          )}
        </Card>
      </section>
      {adding ? <AddToDatasetDialog source={adding} onClose={() => setAdding(null)} /> : null}
    </Page>
  );
}
