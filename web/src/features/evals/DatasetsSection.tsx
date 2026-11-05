import { useMutation, useQueryClient } from '@tanstack/react-query';
import { ArrowRight, CircleCheck, ClipboardCheck, CloudUpload, Copy, ExternalLink, Pencil, Play, Trash2, X } from 'lucide-react';
import { useId, useState } from 'react';
import { Link, Navigate, useNavigate, useSearchParams } from 'react-router';
import { Badge } from '../../components/ui/Badge';
import { Button, buttonClass } from '../../components/ui/Button';
import { CardGrid } from '../../components/ui/Page';
import { Alert, Card, EmptyState, ErrorMessage, Spinner } from '../../components/ui/feedback';
import { Menu } from '../../components/ui/Menu';
import { ConfirmDialog } from '../../components/ui/Modal';
import { toast } from '../../components/ui/Toast';
import { formatNumber, formatRelativeTime, pluralize, safeExternalUrl } from '../../lib/format';
import { evalKeys, evalsApi, useEvalDatasets } from './api';
import { DatasetFormDialog, type DatasetFormMode } from './DatasetFormDialog';
import { datasetPath, latestCompletedRun } from './datasetItems';
import { DatasetSourceBadge } from './EvalBadges';
import { formatPercent } from './metrics';
import { headlineScore, VERDICTS } from './quality';
import { isBuiltinDataset, isCustomDataset, runPath } from './runs';
import { StartRunModal } from './StartRunModal';
import type { EvalDatasetDto, EvalDatasetSyncResultDto, EvalRunSummaryDto } from './types';

function SyncResult({ result, onDismiss }: { result: EvalDatasetSyncResultDto; onDismiss: () => void }) {
  const url = safeExternalUrl(result.langfuseUrl);
  return (
    <div role="status" className="mt-3">
      <Alert tone="accent" icon={CircleCheck} action={<Button variant="ghost" size="icon-sm" icon={X} aria-label="Dismiss" onClick={onDismiss} />}>
        Synced to Langfuse: {formatNumber(result.created)} created · {formatNumber(result.updated)} updated · {formatNumber(result.unchanged)} unchanged ·{' '}
        {formatNumber(result.archived)} archived.
        {url ? (
          <>
            {' '}
            <a href={url} target="_blank" rel="noopener noreferrer" className="inline-flex items-center gap-1 font-medium underline underline-offset-2">
              Open dataset in Langfuse
              <ExternalLink className="size-3" aria-hidden />
            </a>
          </>
        ) : null}
      </Alert>
    </div>
  );
}

function LastRun({ run }: { run: EvalRunSummaryDto | null }) {
  if (!run) {
    return <p className="text-xs text-fg-muted">No recent runs</p>;
  }
  const score = headlineScore(run.metrics);
  const verdict = score ? VERDICTS[score.verdict] : null;
  return (
    <p className="flex flex-wrap items-center gap-x-2 gap-y-1 text-xs text-fg-muted">
      <span>Last run</span>
      {score && verdict ? (
        <>
          <span className="font-medium text-fg tabular-nums" title={score.area.title}>
            {formatPercent(score.value.mean)}
          </span>
          <Badge tone={verdict.tone}>{verdict.label}</Badge>
        </>
      ) : null}
      <Link to={runPath(run.id)} className="text-accent-text hover:underline">
        {formatRelativeTime(run.createdAt)}
      </Link>
    </p>
  );
}

interface DatasetCardProps {
  dataset: EvalDatasetDto;
  lastRun: EvalRunSummaryDto | null;
  langfuseConfigured: boolean;
  onRun: () => void;
  onCopy: () => void;
  onRename: () => void;
  onDelete: () => void;
}

function DatasetCard({ dataset, lastRun, langfuseConfigured, onRun, onCopy, onRename, onDelete }: DatasetCardProps) {
  const queryClient = useQueryClient();
  const headingId = useId();
  const syncHintId = useId();
  const builtin = isBuiltinDataset(dataset.source);
  const custom = isCustomDataset(dataset.source);
  const langfuseUrl = safeExternalUrl(dataset.langfuseUrl);
  const sync = useMutation({
    mutationFn: () => (custom && dataset.id ? evalsApi.syncCustomDataset(dataset.id) : evalsApi.syncEvalDataset(dataset.name)),
    // Picks up the new Langfuse id and link.
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: evalKeys.datasets }),
  });

  const counts = [pluralize(dataset.itemCount, 'question')];
  if (builtin) {
    counts.push(pluralize(dataset.corpusDocuments, 'document'));
  }

  return (
    <li className="min-w-0">
      <section aria-labelledby={headingId} className="flex h-full flex-col rounded-2xl border border-border bg-surface p-4">
        <div className="flex items-start justify-between gap-3">
          <div className="flex min-w-0 items-center gap-3">
            <span aria-hidden className="flex size-10 shrink-0 items-center justify-center rounded-lg bg-fill-muted text-fg-muted">
              <ClipboardCheck className="size-5" />
            </span>
            <div className="min-w-0">
              <h3 id={headingId} className={custom ? 'truncate text-sm font-semibold text-fg' : 'truncate font-mono text-sm font-semibold text-fg'} title={dataset.name}>
                {custom && dataset.id ? (
                  <Link to={datasetPath(dataset.id)} className="hover:underline">
                    {dataset.name}
                  </Link>
                ) : (
                  dataset.name
                )}
              </h3>
              <p className="text-xs text-fg-muted">
                {counts.join(' · ')}
                {builtin ? ' · isolated knowledge base' : ' · live knowledge base'}
                {custom && dataset.updatedAt ? ` · changed ${formatRelativeTime(dataset.updatedAt)}` : ''}
              </p>
            </div>
          </div>
          <div className="flex shrink-0 items-center gap-1">
            <DatasetSourceBadge source={dataset.source} />
            {custom ? (
              <Menu
                label={`Actions for “${dataset.name}”`}
                busy={sync.isPending}
                items={[
                  { label: 'Rename', icon: Pencil, onSelect: onRename },
                  { label: 'Make a copy', icon: Copy, onSelect: onCopy },
                  ...(langfuseConfigured ? [{ label: 'Sync to Langfuse', icon: CloudUpload, onSelect: () => sync.mutate() }] : []),
                  { label: 'Delete', icon: Trash2, onSelect: onDelete, danger: true },
                ]}
              />
            ) : null}
          </div>
        </div>
        {dataset.description ? <p className="mt-3 line-clamp-3 text-small text-fg-secondary">{dataset.description}</p> : null}
        <div className="mt-3">
          <LastRun run={lastRun} />
        </div>
        {dataset.tags.length > 0 ? (
          <ul className="mt-3 flex flex-wrap gap-1" aria-label="Tags">
            {dataset.tags.map((tag) => (
              <li key={tag}>
                <Badge>{tag}</Badge>
              </li>
            ))}
          </ul>
        ) : null}
        <div className="mt-auto pt-4">
          <div className="flex flex-wrap items-center gap-2">
            <Button size="sm" icon={Play} onClick={onRun} disabled={dataset.itemCount === 0}>
              Run
            </Button>
            {custom && dataset.id ? (
              <Link to={datasetPath(dataset.id)} className={buttonClass({ variant: 'ghost', size: 'sm' })}>
                Open questions
                <ArrowRight className="size-3.5" aria-hidden />
              </Link>
            ) : (
              <Button size="sm" variant="ghost" icon={Copy} onClick={onCopy}>
                Make a copy
              </Button>
            )}
            {builtin ? (
              <Button
                size="sm"
                variant="ghost"
                icon={CloudUpload}
                loading={sync.isPending}
                disabled={!langfuseConfigured}
                aria-describedby={langfuseConfigured ? undefined : syncHintId}
                onClick={() => sync.mutate()}
              >
                Sync to Langfuse
              </Button>
            ) : null}
            {langfuseUrl ? (
              <a href={langfuseUrl} target="_blank" rel="noopener noreferrer" className={buttonClass({ variant: 'ghost', size: 'sm' })}>
                Open in Langfuse
                <ExternalLink className="size-3.5" aria-hidden />
              </a>
            ) : null}
          </div>
          {builtin && !langfuseConfigured ? (
            <p id={syncHintId} className="mt-1.5 text-xs text-fg-muted">
              Syncing needs Langfuse.
            </p>
          ) : null}
          {custom && dataset.itemCount === 0 ? <p className="mt-1.5 text-xs text-fg-muted">Open it to add questions, then run it.</p> : null}
          {!custom ? <p className="mt-1.5 text-xs text-fg-muted">Read-only. Make a copy to change the questions.</p> : null}
        </div>
        {sync.isSuccess && sync.data ? <SyncResult result={sync.data} onDismiss={() => sync.reset()} /> : null}
        {sync.isError ? <ErrorMessage error={sync.error} title="Sync to Langfuse failed" className="mt-3" /> : null}
      </section>
    </li>
  );
}

export interface DatasetsSectionProps {
  creating: boolean;
  onCreatingChange: (creating: boolean) => void;
  runs: readonly EvalRunSummaryDto[] | undefined;
}

export function DatasetsSection({ creating, onCreatingChange, runs }: DatasetsSectionProps) {
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const [params] = useSearchParams();
  const datasets = useEvalDatasets();
  const [runFor, setRunFor] = useState<EvalDatasetDto | null>(null);
  const [form, setForm] = useState<DatasetFormMode | null>(null);
  const [deleting, setDeleting] = useState<EvalDatasetDto | null>(null);
  const list = datasets.data;
  const custom = list?.datasets.filter((dataset) => isCustomDataset(dataset.source)) ?? [];
  const templates = list?.datasets.filter((dataset) => !isCustomDataset(dataset.source)) ?? [];
  const shownForm: DatasetFormMode | null = form ?? (creating ? { kind: 'create' } : null);

  const remove = useMutation({
    mutationFn: (dataset: EvalDatasetDto) => evalsApi.deleteDataset(dataset.id!),
    onSuccess: (_data, dataset) => {
      setDeleting(null);
      void queryClient.invalidateQueries({ queryKey: evalKeys.datasets });
      toast({ title: `Deleted ${dataset.name}`, tone: 'success' });
    },
  });

  // Old /evals?dataset={id} links.
  const legacy = params.get('dataset');
  if (legacy) {
    return <Navigate to={datasetPath(legacy)} replace />;
  }

  const card = (dataset: EvalDatasetDto) => (
    <DatasetCard
      key={`${dataset.source}:${dataset.id ?? dataset.name}`}
      dataset={dataset}
      lastRun={latestCompletedRun(dataset, runs)}
      langfuseConfigured={list?.langfuseConfigured ?? false}
      onRun={() => setRunFor(dataset)}
      onCopy={() => setForm({ kind: 'copy', from: dataset })}
      onRename={() => setForm({ kind: 'rename', dataset })}
      onDelete={() => {
        remove.reset();
        setDeleting(dataset);
      }}
    />
  );

  return (
    <section aria-labelledby="eval-datasets-heading" className="space-y-4">
      <div>
        <h2 id="eval-datasets-heading" className="text-section text-fg">
          Datasets
        </h2>
        <p className="mt-0.5 max-w-160 text-sm text-fg-secondary">
          Questions with the answers you expect. A run asks them all and scores the answers, so you see if a change helped. Build one from real conversations, or copy an example.
        </p>
      </div>

      {list && !list.langfuseConfigured ? (
        <Alert tone="info">Langfuse is not configured, so runs are stored only in the portal. With Langfuse, every run is also recorded there.</Alert>
      ) : null}
      {list?.langfuseError ? (
        <Alert tone="warning" title="Langfuse datasets could not be loaded">
          {list.langfuseError}
        </Alert>
      ) : null}

      {datasets.isPending ? (
        <Spinner label="Loading datasets" />
      ) : datasets.isError ? (
        <ErrorMessage error={datasets.error} title="Could not load datasets" />
      ) : (
        <>
          <div className="space-y-2">
            <h3 className="text-sm font-semibold text-fg">Your datasets</h3>
            {custom.length === 0 ? (
              <Card>
                <EmptyState
                  icon={ClipboardCheck}
                  title="No datasets of your own yet"
                  description="Choose New dataset, then add questions from conversations, type them in or upload a file. Questions your visitors really ask make the best tests."
                />
              </Card>
            ) : (
              <CardGrid label="Your datasets">{custom.map(card)}</CardGrid>
            )}
          </div>
          {templates.length > 0 ? (
            <div className="space-y-2">
              <h3 className="text-sm font-semibold text-fg">Examples and Langfuse datasets</h3>
              <p className="text-small text-fg-secondary">
                Built-in examples bring their own documents and run apart from your sources. Make a copy to edit the questions and run them against
                your own sources.
              </p>
              <CardGrid label="Datasets">{templates.map(card)}</CardGrid>
            </div>
          ) : null}
        </>
      )}

      {runFor && list ? (
        <StartRunModal
          key={`${runFor.source}:${runFor.name}`}
          dataset={runFor}
          judgeAvailable={list.judgeAvailable}
          judgeModel={list.judgeModel}
          onClose={() => setRunFor(null)}
        />
      ) : null}
      {shownForm ? (
        <DatasetFormDialog
          mode={shownForm}
          onClose={() => {
            setForm(null);
            onCreatingChange(false);
          }}
          onSaved={(saved) => shownForm.kind !== 'rename' && saved.id && navigate(datasetPath(saved.id))}
        />
      ) : null}
      <ConfirmDialog
        open={deleting !== null}
        title="Delete dataset?"
        message={
          <>
            “{deleting?.name}” and its {pluralize(deleting?.itemCount ?? 0, 'question')} will be deleted. Its past runs stay.
          </>
        }
        confirmLabel="Delete dataset"
        busy={remove.isPending}
        error={remove.isError ? <ErrorMessage error={remove.error} /> : null}
        onConfirm={() => deleting && remove.mutate(deleting)}
        onCancel={() => setDeleting(null)}
      />
    </section>
  );
}
