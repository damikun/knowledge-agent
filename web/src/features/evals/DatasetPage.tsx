import { useMutation, useQueryClient } from '@tanstack/react-query';
import { ClipboardCheck, CloudUpload, Copy, FileUp, Keyboard, MessagesSquare, Pencil, Play, Plus, Trash2 } from 'lucide-react';
import { useState, type ReactNode } from 'react';
import { Link, useNavigate, useParams } from 'react-router';
import { Badge } from '../../components/ui/Badge';
import { Button } from '../../components/ui/Button';
import { Alert, Card, EmptyState, ErrorMessage, PageHeader, Spinner } from '../../components/ui/feedback';
import { Menu, type MenuItem } from '../../components/ui/Menu';
import { ConfirmDialog } from '../../components/ui/Modal';
import { Page } from '../../components/ui/Page';
import { StatCard } from '../../components/ui/StatCard';
import { toast } from '../../components/ui/Toast';
import { useBreadcrumb } from '../../lib/breadcrumb';
import { formatNumber, formatRelativeTime, pluralize } from '../../lib/format';
import { evalKeys, evalsApi, useEvalDatasetItems, useEvalDatasets, useEvalRuns } from './api';
import { ConversationQuestionsSheet } from './ConversationQuestionsSheet';
import { DatasetFormDialog, type DatasetFormMode } from './DatasetFormDialog';
import { DatasetQuestions } from './DatasetQuestions';
import { datasetPath, existingKeys, ITEM_SOURCE_LABELS, latestCompletedRun, SOURCE_ORDER, sourceCounts } from './datasetItems';
import { DatasetSourceBadge } from './EvalBadges';
import { formatPercent } from './metrics';
import { headlineScore, VERDICTS } from './quality';
import { QuestionSheet } from './QuestionSheet';
import { isBuiltinDataset, isCustomDataset, runPath } from './runs';
import { StartRunModal } from './StartRunModal';
import type { EvalDatasetDto, EvalDatasetItemDto, EvalDatasetListDto, ImportEvalItemsResultDto } from './types';
import { UploadQuestionsSheet } from './UploadQuestionsSheet';

type AddMode = 'conversations' | 'manual' | 'upload';

const ADD_CHOICES: ReadonlyArray<{ mode: AddMode; icon: typeof Plus; title: string; description: string }> = [
  {
    mode: 'conversations',
    icon: MessagesSquare,
    title: 'From conversations',
    description: 'Pick real questions, such as poorly rated ones or those handed to a person.',
  },
  { mode: 'manual', icon: Keyboard, title: 'Type one in', description: 'Write a question, the expected answer and the key facts.' },
  { mode: 'upload', icon: FileUp, title: 'Upload a file', description: 'Add many at once from a CSV or JSONL file.' },
];

function addedToast(result: ImportEvalItemsResultDto) {
  toast({
    title: result.created === 0 ? 'No questions added' : `Added ${pluralize(result.created, 'question')}`,
    description: result.skipped > 0 ? `${pluralize(result.skipped, 'question')} the dataset already had ${result.skipped === 1 ? 'was' : 'were'} left out.` : undefined,
    tone: result.created === 0 ? undefined : 'success',
  });
}

function LastRunCard({ dataset }: { dataset: EvalDatasetDto }) {
  const runs = useEvalRuns({ dataset: dataset.name, limit: 20 }, { poll: false });
  const last = latestCompletedRun(dataset, runs.data);
  const score = last ? headlineScore(last.metrics) : null;
  if (!last) {
    return <StatCard label="Last run" value="Not run yet" caption={runs.isPending ? 'Loading runs' : 'Run it to see how the agent does.'} className="h-full" />;
  }
  const verdict = score ? VERDICTS[score.verdict] : null;
  return (
    <StatCard
      label={score ? `Last run: ${score.area.title.toLowerCase()}` : 'Last run'}
      value={score ? formatPercent(score.value.mean) : 'No scores'}
      caption={
        <>
          {verdict ? <Badge tone={verdict.tone}>{verdict.label}</Badge> : null}
          <Link to={runPath(last.id)} className="font-medium text-accent-text hover:underline">
            {formatRelativeTime(last.createdAt)}
          </Link>
        </>
      }
      className="h-full"
    />
  );
}

function AddChooser({ onChoose }: { onChoose: (mode: AddMode) => void }) {
  return (
    <ul className="grid gap-3 sm:grid-cols-3" aria-label="Ways to add questions">
      {ADD_CHOICES.map(({ mode, icon: Icon, title, description }) => (
        <li key={mode}>
          <button
            type="button"
            onClick={() => onChoose(mode)}
            className="flex h-full w-full flex-col items-start gap-2 rounded-xl border border-border bg-surface p-4 text-left hover:border-border-hover"
          >
            <span aria-hidden className="flex size-8 items-center justify-center rounded-lg bg-fill-muted text-fg-muted">
              <Icon className="size-4" />
            </span>
            <span className="text-sm font-medium text-fg">{title}</span>
            <span className="text-small text-fg-secondary">{description}</span>
          </button>
        </li>
      ))}
    </ul>
  );
}

function CustomDataset({ dataset, list }: { dataset: EvalDatasetDto; list: EvalDatasetListDto }) {
  const id = dataset.id!;
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const items = useEvalDatasetItems(id);
  const [adding, setAdding] = useState<AddMode | null>(null);
  const [editing, setEditing] = useState<EvalDatasetItemDto | null>(null);
  const [form, setForm] = useState<DatasetFormMode | null>(null);
  const [deletingDataset, setDeletingDataset] = useState(false);
  const [running, setRunning] = useState(false);

  const all = items.data ?? [];
  const counts = sourceCounts(all);
  const declines = all.filter((item) => !item.answerable).length;
  const refresh = () => void queryClient.invalidateQueries({ queryKey: evalKeys.datasets });

  const sync = useMutation({
    mutationFn: () => evalsApi.syncCustomDataset(id),
    onSuccess: () => {
      refresh();
      toast({ title: 'Synced to Langfuse', tone: 'success' });
    },
    onError: () => toast({ title: 'Sync to Langfuse failed', tone: 'danger' }),
  });
  const removeDataset = useMutation({
    mutationFn: () => evalsApi.deleteDataset(id),
    onSuccess: () => {
      refresh();
      toast({ title: `Deleted ${dataset.name}`, tone: 'success' });
      navigate('/evals');
    },
  });

  const more: MenuItem[] = [
    { label: 'Rename', icon: Pencil, onSelect: () => setForm({ kind: 'rename', dataset }) },
    { label: 'Make a copy', icon: Copy, onSelect: () => setForm({ kind: 'copy', from: dataset }) },
    ...(list.langfuseConfigured ? [{ label: 'Sync to Langfuse', icon: CloudUpload, onSelect: () => sync.mutate() }] : []),
    {
      label: 'Delete dataset',
      icon: Trash2,
      danger: true,
      onSelect: () => {
        removeDataset.reset();
        setDeletingDataset(true);
      },
    },
  ];

  let content: ReactNode;
  if (items.isPending) {
    content = <Spinner label="Loading the questions" />;
  } else if (items.isError) {
    content = <ErrorMessage error={items.error} title="Could not load the questions" />;
  } else if (all.length === 0) {
    content = (
      <Card className="p-6">
        <div className="mx-auto max-w-180 space-y-4">
          <div className="text-center">
            <h2 className="text-section text-fg">Add the first questions</h2>
            <p className="mt-1 text-sm text-fg-secondary">Ten to fifty questions your visitors really ask make a good start. Choose how to add them.</p>
          </div>
          <AddChooser onChoose={setAdding} />
        </div>
      </Card>
    );
  } else {
    content = <DatasetQuestions datasetId={id} items={all} onChanged={refresh} onEdit={setEditing} />;
  }

  const keys = existingKeys(items.data);
  const onAdded = (result: ImportEvalItemsResultDto) => {
    refresh();
    addedToast(result);
  };

  return (
    <Page>
      <PageHeader
        title={dataset.name}
        description={
          dataset.description ?? 'Questions with the answers you expect. A run asks the agent each one against your live sources and scores the answers.'
        }
        actions={
          <>
            <Menu label={`More actions for “${dataset.name}”`} items={more} busy={sync.isPending} />
            <Button icon={Play} onClick={() => setRunning(true)} disabled={all.length === 0}>
              Run
            </Button>
            <Menu
              label="Add questions"
              items={ADD_CHOICES.map(({ mode, icon, title }) => ({ label: title, icon, onSelect: () => setAdding(mode) }))}
              trigger={(props) => (
                <Button {...props} variant="primary" icon={Plus}>
                  Add questions
                </Button>
              )}
            />
          </>
        }
      />
      <ul className="grid gap-3 sm:grid-cols-3" aria-label="Summary">
        <li>
          <StatCard
            label="Questions"
            value={formatNumber(items.data?.length ?? dataset.itemCount)}
            caption={
              all.length > 0
                ? SOURCE_ORDER.filter((key) => counts[key] > 0)
                    .map((key) => `${formatNumber(counts[key])} ${ITEM_SOURCE_LABELS[key].toLowerCase()}`)
                    .join(' · ')
                : 'None yet'
            }
            className="h-full"
          />
        </li>
        <li>
          <LastRunCard dataset={dataset} />
        </li>
        <li>
          <StatCard
            label="Should say “I don’t know”"
            value={formatNumber(declines)}
            caption={declines === 0 ? 'Add a few questions your sources do not cover, so runs check it does not guess.' : 'Questions it should decline'}
            className="h-full"
          />
        </li>
      </ul>
      {content}

      {adding === 'manual' ? (
        <QuestionSheet
          datasetId={id}
          item={null}
          onClose={() => setAdding(null)}
          onSaved={() => {
            refresh();
            toast({ title: 'Question added', tone: 'success' });
          }}
        />
      ) : null}
      {adding === 'conversations' ? <ConversationQuestionsSheet datasetId={id} existing={keys} onClose={() => setAdding(null)} onAdded={onAdded} /> : null}
      {adding === 'upload' ? <UploadQuestionsSheet datasetId={id} existing={keys} onClose={() => setAdding(null)} onAdded={onAdded} /> : null}
      {editing ? (
        <QuestionSheet
          key={editing.id}
          datasetId={id}
          item={editing}
          onClose={() => setEditing(null)}
          onSaved={() => {
            refresh();
            toast({ title: 'Question saved', tone: 'success' });
          }}
        />
      ) : null}
      {running ? (
        <StartRunModal dataset={dataset} judgeAvailable={list.judgeAvailable} judgeModel={list.judgeModel} onClose={() => setRunning(false)} />
      ) : null}
      {form ? (
        <DatasetFormDialog
          mode={form}
          onClose={() => setForm(null)}
          onSaved={(saved) => {
            if (form.kind === 'copy' && saved.id) {
              navigate(datasetPath(saved.id));
            }
          }}
        />
      ) : null}
      <ConfirmDialog
        open={deletingDataset}
        title="Delete dataset?"
        message={<>“{dataset.name}” and its {pluralize(all.length || dataset.itemCount, 'question')} will be deleted. Its past runs stay.</>}
        confirmLabel="Delete dataset"
        busy={removeDataset.isPending}
        error={removeDataset.isError ? <ErrorMessage error={removeDataset.error} /> : null}
        onConfirm={() => removeDataset.mutate()}
        onCancel={() => setDeletingDataset(false)}
      />
    </Page>
  );
}

function ReadOnlyDataset({ dataset, list }: { dataset: EvalDatasetDto; list: EvalDatasetListDto }) {
  const navigate = useNavigate();
  const [copying, setCopying] = useState(false);
  const [running, setRunning] = useState(false);
  const builtin = isBuiltinDataset(dataset.source);
  return (
    <Page>
      <PageHeader
        title={dataset.name}
        description={dataset.description ?? undefined}
        actions={
          <>
            <Button icon={Play} onClick={() => setRunning(true)} disabled={dataset.itemCount === 0}>
              Run
            </Button>
            <Button variant="primary" icon={Copy} onClick={() => setCopying(true)}>
              Make a copy
            </Button>
          </>
        }
      />
      <Alert tone="info" title={builtin ? 'A built-in example' : 'Managed in Langfuse'}>
        {builtin
          ? 'Its questions cannot be changed here. Make a copy to edit them and run them against your own sources.'
          : 'Its questions are edited in Langfuse. Make a copy to edit them here.'}
      </Alert>
      <ul className="grid gap-3 sm:grid-cols-3" aria-label="Summary">
        <li>
          <StatCard label="Questions" value={formatNumber(dataset.itemCount)} caption={<DatasetSourceBadge source={dataset.source} />} className="h-full" />
        </li>
        <li>
          <LastRunCard dataset={dataset} />
        </li>
      </ul>
      {copying ? (
        <DatasetFormDialog mode={{ kind: 'copy', from: dataset }} onClose={() => setCopying(false)} onSaved={(saved) => saved.id && navigate(datasetPath(saved.id))} />
      ) : null}
      {running ? <StartRunModal dataset={dataset} judgeAvailable={list.judgeAvailable} judgeModel={list.judgeModel} onClose={() => setRunning(false)} /> : null}
    </Page>
  );
}

export function DatasetPage() {
  const { id = '' } = useParams();
  const datasets = useEvalDatasets();
  const all = datasets.data?.datasets ?? [];
  const dataset =
    all.find((candidate) => isCustomDataset(candidate.source) && candidate.id === id) ??
    all.find((candidate) => !isCustomDataset(candidate.source) && candidate.name === id) ??
    null;
  useBreadcrumb(dataset?.name ?? null);

  if (datasets.isPending) {
    return (
      <Page>
        <Spinner label="Loading the dataset" />
      </Page>
    );
  }
  if (datasets.isError) {
    return (
      <Page>
        <ErrorMessage error={datasets.error} title="Could not load the dataset" />
      </Page>
    );
  }
  if (!dataset) {
    return (
      <Page>
        <Card>
          <EmptyState
            icon={ClipboardCheck}
            title="Dataset not found"
            description="It may have been deleted."
            action={
              <Link to="/evals" className="text-sm font-medium text-accent-text hover:underline">
                Back to Evaluations
              </Link>
            }
          />
        </Card>
      </Page>
    );
  }
  return isCustomDataset(dataset.source) ? <CustomDataset key={dataset.id} dataset={dataset} list={datasets.data} /> : <ReadOnlyDataset dataset={dataset} list={datasets.data} />;
}
