import { useQueryClient } from '@tanstack/react-query';
import { SearchX } from 'lucide-react';
import { useMemo, useRef, useState, type ReactNode } from 'react';
import { Link, useParams } from 'react-router';
import { buttonClass } from '../../components/ui/Button';
import { Alert, Card, EmptyState, ErrorMessage, Spinner } from '../../components/ui/feedback';
import { Page } from '../../components/ui/Page';
import { ApiError } from '../../lib/api';
import { useBreadcrumb } from '../../lib/breadcrumb';
import { formatDateTime, formatDuration, formatNumber, formatRelativeTime, pluralize } from '../../lib/format';
import { useOnChange } from '../../lib/hooks/useOnChange';
import { evalKeys, useEvalRun, useEvalRuns } from './api';
import { RunProgress, RunStatusBadge } from './EvalBadges';
import { isRunActive, normalizeRunStatus } from './polling';
import { filterItems, itemAreaMetrics, type ItemFilter, type QualityAreaId } from './quality';
import { Fact, Missing, RunConfiguration, SectionTitle } from './RunFacts';
import { RunHeader } from './RunHeader';
import { ItemsTable } from './RunItems';
import { GateChecks, QualityCards, TagTable, TechnicalDetails } from './RunMetrics';
import { previousCompletedRun, runDurationMs, runPath, RUNS_LIMIT } from './runs';
import type { EvalRunDto } from './types';
import { scrollBehavior } from '../../lib/motion';

function RunDetails({ run, now }: { run: EvalRunDto; now: number }) {
  const queryClient = useQueryClient();
  const { summary } = run;
  const status = normalizeRunStatus(summary.status);
  const active = isRunActive(summary.status);
  // Not polled; the run itself is.
  const history = useEvalRuns({ dataset: summary.dataset, limit: RUNS_LIMIT }, { poll: false });
  const previous = previousCompletedRun(summary, history.data);
  const [itemFilter, setItemFilter] = useState<ItemFilter>('all');
  const itemsRef = useRef<HTMLElement>(null);
  const missed = useMemo(() => {
    const metrics = itemAreaMetrics(run.items);
    const counts: Partial<Record<QualityAreaId, number>> = {};
    for (const area of Object.keys(metrics) as QualityAreaId[]) {
      counts[area] = filterItems(run.items, area, metrics).length;
    }
    return counts;
  }, [run.items]);
  const showMissed = (area: QualityAreaId) => {
    setItemFilter(area);
    itemsRef.current?.scrollIntoView({ behavior: scrollBehavior(), block: 'start' });
  };

  // The run lists may still show it as running.
  useOnChange(active, (stillActive) => {
    if (!stillActive) {
      void queryClient.invalidateQueries({ queryKey: evalKeys.runLists });
    }
  });

  return (
    <>
      <RunHeader run={run} />

      <Card>
        <dl className="grid gap-x-6 gap-y-3 px-5 py-4 sm:grid-cols-2 lg:grid-cols-4">
          <Fact label="Status">
            <span className="inline-flex flex-wrap items-center gap-2">
              <RunStatusBadge run={summary} />
              {active ? <Spinner label="Refreshing every 2 s" className="size-3.5" /> : null}
            </span>
          </Fact>
          <Fact label="Questions">
            {active ? null : (
              <span className="tabular-nums">
                {formatNumber(summary.completedItems)} of {pluralize(summary.totalItems, 'question')}
              </span>
            )}
            <RunProgress run={summary} className={active ? undefined : 'mt-0.5'} />
          </Fact>
          <Fact label="Started">{summary.startedAt ? formatDateTime(summary.startedAt) : <Missing>Not started yet</Missing>}</Fact>
          <Fact label="Duration">{formatDuration(runDurationMs(summary, now))}</Fact>
        </dl>
        {summary.error ? (
          <div className="border-t border-border-subtle px-5 py-3">
            <Alert tone={status === 'failed' ? 'danger' : 'warning'} title={status === 'failed' ? 'The run failed' : undefined}>
              {summary.error}
            </Alert>
          </div>
        ) : null}
      </Card>

      {run.warnings.length > 0 ? (
        <Alert tone="warning" title={run.warnings.length === 1 ? 'Warning' : `${formatNumber(run.warnings.length)} warnings`}>
          <ul className="list-disc space-y-0.5 pl-4 text-sm" aria-label="Run warnings">
            {run.warnings.map((warning, index) => (
              <li key={index}>{warning}</li>
            ))}
          </ul>
        </Alert>
      ) : null}

      <section aria-labelledby="run-metrics-heading" className="@container space-y-3">
        <SectionTitle
          id="run-metrics-heading"
          title="How the agent did"
          aside={
            <>
              {summary.completedItems > 0 ? `${pluralize(summary.completedItems, 'question')} scored` : null}
              {previous ? (
                <>
                  {summary.completedItems > 0 ? ' · ' : null}
                  compared with{' '}
                  <Link to={runPath(previous.id)} className="font-medium text-accent-text hover:underline">
                    {previous.name || 'the previous run'}
                  </Link>{' '}
                  ({formatRelativeTime(previous.createdAt)})
                </>
              ) : history.isSuccess ? (
                <>{summary.completedItems > 0 ? ' · ' : null}no earlier run of this dataset to compare with</>
              ) : null}
            </>
          }
        />
        <QualityCards metrics={summary.metrics} previous={previous} active={active} missed={missed} onShowMissed={showMissed} />
        <TechnicalDetails metrics={summary.metrics} previous={previous} />
      </section>

      {summary.gate ? (
        <section aria-labelledby="run-gate-heading">
          <GateChecks gate={summary.gate} />
        </section>
      ) : null}

      {run.byTag.length > 0 ? (
        <section aria-labelledby="run-tags-heading" className="space-y-3">
          <SectionTitle id="run-tags-heading" title="By tag" aside="Find the topics that pull the scores down" />
          <Card>
            <TagTable byTag={run.byTag} overall={summary.metrics} totalItems={summary.totalItems} />
          </Card>
        </section>
      ) : null}

      <RunConfiguration run={run} />

      <section ref={itemsRef} aria-labelledby="run-items-heading" className="scroll-mt-4 space-y-3">
        <SectionTitle
          id="run-items-heading"
          title="Questions"
          aside={
            run.items.length > 0
              ? `${pluralize(run.items.length, 'result')}${summary.failedItems > 0 ? ` · ${formatNumber(summary.failedItems)} failed` : ''} · expand a question for its answer`
              : null
          }
        />
        <Card>
          <ItemsTable items={run.items} active={active} filter={itemFilter} onFilterChange={setItemFilter} />
        </Card>
      </section>
    </>
  );
}

export function EvalRunPage() {
  const { id: routeId } = useParams();
  const id = routeId ?? null;
  const run = useEvalRun(id);
  useBreadcrumb(run.data ? run.data.summary.name || 'Untitled run' : null);

  let content: ReactNode;
  if (run.isPending) {
    content = (
      <div className="py-10 text-center">
        <Spinner label="Loading run" />
      </div>
    );
  } else if (run.isError) {
    content =
      run.error instanceof ApiError && run.error.status === 404 ? (
        <Card>
          <EmptyState
            icon={SearchX}
            title="Run not found"
            headingLevel={1}
            description="It may have been deleted."
            action={
              <Link to="/evals" className={buttonClass()}>
                Show all runs
              </Link>
            }
          />
        </Card>
      ) : (
        <ErrorMessage error={run.error} title="Could not load the run" />
      );
  } else {
    content = <RunDetails run={run.data} now={run.dataUpdatedAt || Date.now()} />;
  }

  return <Page>{content}</Page>;
}
