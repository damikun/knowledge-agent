import { useMutation, useQueryClient } from '@tanstack/react-query';
import { History, Plus, RefreshCw } from 'lucide-react';
import { useState, type ReactNode } from 'react';
import { Link } from 'react-router';
import { Button } from '../../components/ui/Button';
import { Card, EmptyState, ErrorMessage, PageHeader, Spinner } from '../../components/ui/feedback';
import { ConfirmDialog } from '../../components/ui/Modal';
import { StatCard, type StatTrend } from '../../components/ui/StatCard';
import { StatusDot } from '../../components/ui/StatusDot';
import { Page } from '../../components/ui/Page';
import { formatNumber, formatRelativeTime, pluralize } from '../../lib/format';
import { evalKeys, evalsApi, useEvalRuns } from './api';
import { DatasetsSection } from './DatasetsSection';
import { Badge } from '../../components/ui/Badge';
import { formatDelta, formatPercent, metricInfo } from './metrics';
import { scoreAreas, VERDICTS } from './quality';
import { anyRunActive, normalizeRunStatus } from './polling';
import { previousCompletedRun, runPath, RUNS_LIMIT } from './runs';
import { RunsTable } from './RunsTable';
import type { EvalGateDto, EvalRunSummaryDto } from './types';

function gateSummary(gate: EvalGateDto | null): { value: ReactNode; caption: string } {
  if (!gate) {
    return { value: 'None', caption: 'This run had no quality gate' };
  }
  const failed = gate.checks.filter((check) => !check.passed).length;
  return {
    value: (
      <span className="inline-flex items-center gap-2">
        <StatusDot tone={gate.passed ? 'success' : 'danger'} className="size-2.5" />
        {gate.passed ? 'Passed' : 'Failed'}
      </span>
    ),
    caption: gate.checks.length === 0 ? 'No checks configured' : gate.passed ? `All ${pluralize(gate.checks.length, 'check')} passed` : `${formatNumber(failed)} of ${pluralize(gate.checks.length, 'check')} failed`,
  };
}

function LatestRun({ runs }: { runs: EvalRunSummaryDto[] }) {
  const latest = runs.find((run) => normalizeRunStatus(run.status) === 'completed');
  if (!latest) {
    return null;
  }
  const previous = previousCompletedRun(latest, runs);
  const scores = scoreAreas(latest.metrics, previous?.metrics).slice(0, 3);
  const gate = gateSummary(latest.gate);

  return (
    <section aria-labelledby="eval-latest-heading" className="@container space-y-3">
      <div className="flex flex-wrap items-baseline justify-between gap-x-4 gap-y-1">
        <h2 id="eval-latest-heading" className="text-section text-fg">
          Latest results
        </h2>
        <p className="text-small text-fg-secondary">
          <Link to={runPath(latest.id)} className="font-medium text-accent-text hover:underline">
            {latest.name || 'Untitled run'}
          </Link>
          <span className="text-fg-muted"> · {formatRelativeTime(latest.createdAt)}</span>
          {previous ? <span className="text-fg-muted"> · compared with {previous.name || 'the run before'}</span> : null}
        </p>
      </div>
      <ul aria-label="Latest results" className="grid gap-3 @lg:grid-cols-2 @3xl:grid-cols-4">
        <li>
          <StatCard label="Quality gate" value={gate.value} caption={gate.caption} className="h-full" />
        </li>
        {scores.map((score) => {
          const result = VERDICTS[score.verdict];
          const trend: StatTrend | undefined = !score.change
            ? undefined
            : score.change.kind === 'same'
              ? { direction: 'flat', label: 'Same as last run', tone: 'neutral' }
              : {
                  direction: score.change.kind === 'better' ? 'up' : 'down',
                  label: formatDelta(score.change.points),
                  tone: score.change.kind === 'better' ? 'success' : 'danger',
                };
          return (
            <li key={score.area.id} data-area={score.area.id} title={`${score.area.meaning} Measured as: ${metricInfo(score.metric).label}.`}>
              <StatCard
                label={score.area.title}
                value={formatPercent(score.value.mean)}
                trend={trend}
                caption={<Badge tone={result.tone}>{result.label}</Badge>}
                className="h-full"
              />
            </li>
          );
        })}
      </ul>
    </section>
  );
}

function RunsSection({ runs }: { runs: ReturnType<typeof useEvalRuns> }) {
  const queryClient = useQueryClient();
  const [pendingDelete, setPendingDelete] = useState<EvalRunSummaryDto | null>(null);
  const [cancelling, setCancelling] = useState<ReadonlySet<string>>(new Set());
  const [refreshing, setRefreshing] = useState(false);
  const polling = anyRunActive(runs.data);

  const showUpdated = (updated: EvalRunSummaryDto | undefined) => {
    if (updated) {
      queryClient.setQueriesData<EvalRunSummaryDto[]>({ queryKey: evalKeys.runLists }, (old) =>
        old?.map((run) => (run.id === updated.id ? updated : run)),
      );
    }
  };

  const cancel = useMutation({
    mutationFn: (run: EvalRunSummaryDto) => evalsApi.cancelEvalRun(run.id),
    onMutate: (run) => setCancelling((current) => new Set(current).add(run.id)),
    onSuccess: showUpdated,
    onSettled: (_data, _error, run) => {
      setCancelling((current) => {
        const next = new Set(current);
        next.delete(run.id);
        return next;
      });
      void queryClient.invalidateQueries({ queryKey: evalKeys.runs });
    },
  });

  const remove = useMutation({
    mutationFn: (run: EvalRunSummaryDto) => evalsApi.deleteEvalRun(run.id),
    onSuccess: (_data, run) => {
      setPendingDelete(null);
      queryClient.setQueriesData<EvalRunSummaryDto[]>({ queryKey: evalKeys.runLists }, (old) => old?.filter((r) => r.id !== run.id));
      queryClient.removeQueries({ queryKey: evalKeys.run(run.id), exact: true });
      void queryClient.invalidateQueries({ queryKey: evalKeys.runLists });
    },
  });

  const refresh = () => {
    setRefreshing(true);
    void queryClient.invalidateQueries({ queryKey: evalKeys.runLists }).finally(() => setRefreshing(false));
  };

  let content: ReactNode;
  if (runs.isPending) {
    content = (
      <div className="px-6 py-8 text-center">
        <Spinner label="Loading runs" />
      </div>
    );
  } else if (runs.isError) {
    content = (
      <div className="p-4">
        <ErrorMessage error={runs.error} title="Could not load runs" />
      </div>
    );
  } else if (runs.data.length === 0) {
    content = <EmptyState icon={History} title="No runs yet" description="Choose Run on a dataset above to ask its questions and score the answers." />;
  } else {
    content = (
      <RunsTable
        runs={runs.data}
        now={runs.dataUpdatedAt || Date.now()}
        busyIds={cancelling}
        onCancel={(run) => cancel.mutate(run)}
        onDelete={(run) => {
          remove.reset();
          setPendingDelete(run);
        }}
      />
    );
  }

  return (
    <section aria-labelledby="eval-runs-heading" className="space-y-3">
      <div className="flex flex-wrap items-end justify-between gap-x-4 gap-y-2">
        <div>
          <h2 id="eval-runs-heading" className="text-section text-fg">
            Runs
          </h2>
          <p className="mt-0.5 text-sm text-fg-secondary">Newest first. Open a run for its metrics, quality gate and answers.</p>
        </div>
        <div className="flex items-center gap-2 text-xs">
          {polling ? <Spinner label="Refreshing every 2 s" className="size-3.5" /> : null}
          <Button size="sm" variant="ghost" icon={RefreshCw} onClick={refresh} loading={refreshing}>
            Refresh
          </Button>
        </div>
      </div>
      <Card className="overflow-hidden">
        {cancel.isError ? <ErrorMessage error={cancel.error} title="The run was not cancelled" className="m-4" /> : null}
        {content}
        {runs.data && runs.data.length >= RUNS_LIMIT ? (
          <p className="border-t border-border-subtle px-6 py-2.5 text-xs text-fg-muted">Showing the {formatNumber(runs.data.length)} most recent runs.</p>
        ) : null}
      </Card>
      <ConfirmDialog
        open={pendingDelete !== null}
        title="Delete run?"
        message={
          <>
            “{pendingDelete?.name || 'Untitled run'}” and its results will be removed from the portal.
            {pendingDelete?.langfuseUrl ? ' The experiment recorded in Langfuse is kept.' : ''}
          </>
        }
        confirmLabel="Delete run"
        busy={remove.isPending}
        error={remove.isError ? <ErrorMessage error={remove.error} /> : null}
        onConfirm={() => pendingDelete && remove.mutate(pendingDelete)}
        onCancel={() => setPendingDelete(null)}
      />
    </section>
  );
}

export function EvalsPage() {
  const runs = useEvalRuns({ limit: RUNS_LIMIT });
  const [creating, setCreating] = useState(false);
  return (
    <Page>
      <PageHeader
        title="Evaluations"
        description="Test the agent with questions whose answers you know, and see whether a change to sources, prompts or models made it better or worse."
        actions={
          <Button variant="primary" icon={Plus} onClick={() => setCreating(true)}>
            New dataset
          </Button>
        }
      />
      {runs.data ? <LatestRun runs={runs.data} /> : null}
      <DatasetsSection creating={creating} onCreatingChange={setCreating} runs={runs.data} />
      <RunsSection runs={runs} />
    </Page>
  );
}
