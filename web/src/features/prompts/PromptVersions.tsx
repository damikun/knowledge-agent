import { clsx } from 'clsx';
import { History, Pencil } from 'lucide-react';
import { useMemo, useState } from 'react';
import { Badge } from '../../components/ui/Badge';
import { Button } from '../../components/ui/Button';
import { ConfirmDialog } from '../../components/ui/Modal';
import { toast } from '../../components/ui/Toast';
import { EmptyState, ErrorMessage } from '../../components/ui/feedback';
import { Skeleton } from '../../components/ui/Skeleton';
import { formatNumber } from '../../lib/format';
import { usePromptVersions, usePublishPrompt } from './api';
import { diffLines, diffStats, promptText, type DiffLine } from './diff';
import type { PromptVersionDto } from './types';

const LINE_CLASSES: Record<DiffLine['type'], string> = {
  same: 'text-fg-secondary',
  added: 'bg-success-subtle text-success-text',
  removed: 'bg-danger-subtle text-danger-text',
};

const LINE_MARKS: Record<DiffLine['type'], string> = { same: ' ', added: '+', removed: '-' };

function LineDiff({ before, after }: { before: string; after: string }) {
  const lines = useMemo(() => diffLines(before, after), [before, after]);
  const stats = diffStats(lines);
  if (stats.added === 0 && stats.removed === 0) {
    return <p className="text-small text-fg-muted">Same content as the live version.</p>;
  }
  return (
    <div className="space-y-1.5">
      <p className="text-xs text-fg-muted tabular-nums">
        <span className="text-success-text">+{formatNumber(stats.added)}</span> <span className="text-danger-text">-{formatNumber(stats.removed)}</span> lines against the live version
      </p>
      <pre aria-label="Changes against the live version" className="overflow-auto rounded-lg border border-border-subtle bg-surface-subtle py-2 font-mono text-xs leading-5 break-words whitespace-pre-wrap">
        {lines.map((line, index) => (
          <div key={index} className={clsx('px-3', LINE_CLASSES[line.type])}>
            <span aria-hidden className="mr-2 inline-block w-2 select-none">
              {LINE_MARKS[line.type]}
            </span>
            {line.type !== 'same' ? <span className="sr-only">{line.type === 'added' ? 'Added: ' : 'Removed: '}</span> : null}
            {line.text || ' '}
          </div>
        ))}
      </pre>
    </div>
  );
}

function VersionRow({ version, liveLabel, selected, onSelect }: { version: PromptVersionDto; liveLabel: string; selected: boolean; onSelect: () => void }) {
  const labels = version.labels.filter((label) => label !== 'latest');
  return (
    <li>
      <button
        type="button"
        aria-pressed={selected}
        onClick={onSelect}
        className={clsx('w-full rounded-lg px-3 py-2 text-left transition-colors', selected ? 'bg-fill-active text-fg' : 'text-fg-secondary hover:bg-fill-hover')}
      >
        <span className="flex flex-wrap items-center gap-1.5">
          <span className="text-small font-medium text-fg">Version {version.version}</span>
          {version.live ? <Badge tone="success">Live</Badge> : null}
          {labels
            .filter((label) => !version.live || label !== liveLabel)
            .map((label) => (
              <Badge key={label}>{label}</Badge>
            ))}
        </span>
        {version.commitMessage ? <span className="mt-0.5 block truncate text-xs text-fg-muted">{version.commitMessage}</span> : null}
      </button>
    </li>
  );
}

function LoadingVersions() {
  return (
    <div role="status" className="space-y-2">
      <span className="sr-only">Loading versions</span>
      <Skeleton className="h-10 w-full" />
      <Skeleton className="h-10 w-full" />
      <Skeleton className="h-10 w-3/4" />
    </div>
  );
}

/** Publishing a newer draft or rolling back to an older version both move the live label. */
export function PromptVersions({
  name,
  onEdit,
}: {
  name: string;
  onEdit: (version: PromptVersionDto, liveVersion: number | null, label: string) => void;
}) {
  const versions = usePromptVersions(name);
  const publish = usePublishPrompt(name);
  const [selectedVersion, setSelectedVersion] = useState<number | null>(null);
  const [confirming, setConfirming] = useState(false);

  if (versions.isPending) {
    return <LoadingVersions />;
  }
  if (versions.isError) {
    return <ErrorMessage error={versions.error} title="Could not load the versions from Langfuse" />;
  }

  const data = versions.data;
  if (!data.editable) {
    return <EmptyState icon={History} title="No versions here" description="Prompts are not stored in Langfuse on this server, so the built-in defaults are used and cannot be edited." />;
  }

  const live = data.versions.find((version) => version.live) ?? null;
  // Select a new draft first if there is one, else the live version.
  const selected =
    data.versions.find((version) => version.version === selectedVersion) ??
    data.versions.find((version) => live && version.version > live.version) ??
    live ??
    data.versions[0];
  if (!selected) {
    return <EmptyState icon={History} title="No versions" description="Langfuse has no versions of this prompt." />;
  }

  const rollBack = live !== null && selected.version < live.version;
  const action = rollBack ? `Roll back to version ${selected.version}` : `Publish version ${selected.version}`;

  const confirm = () =>
    publish.mutate(
      { version: selected.version, expectedLiveVersion: data.liveVersion },
      {
        onSuccess: (result) => {
          setConfirming(false);
          toast({
            title: rollBack ? `Rolled back to version ${result.version}` : `Version ${result.version} is live`,
            description: `The ${result.label} label moved${result.previousVersion ? ` from version ${result.previousVersion}` : ''}. New answers use it now.`,
            tone: 'success',
          });
        },
      },
    );

  return (
    <div className="@container">
      <div className="grid gap-5 @2xl:grid-cols-[16rem_minmax(0,1fr)]">
        <div className="min-w-0 space-y-2">
          <p className="text-xs text-fg-muted">
            {data.totalVersions > data.versions.length ? `The newest ${formatNumber(data.versions.length)} of ${formatNumber(data.totalVersions)} versions.` : `${formatNumber(data.totalVersions)} versions.`} The live one has the{' '}
            <code className="font-mono">{data.label}</code> label.
          </p>
          <ul aria-label="Versions" className="space-y-1">
            {data.versions.map((version) => (
              <VersionRow
                key={version.version}
                version={version}
                liveLabel={data.label}
                selected={version.version === selected.version}
                onSelect={() => {
                  setSelectedVersion(version.version);
                  publish.reset();
                }}
              />
            ))}
          </ul>
        </div>

        <section aria-label={`Version ${selected.version}`} className="min-w-0 space-y-3">
          <div className="flex flex-wrap items-center justify-between gap-2">
            <h3 className="text-section text-fg">Version {selected.version}</h3>
            <div className="flex flex-wrap gap-2">
              <Button size="sm" icon={Pencil} onClick={() => onEdit(selected, data.liveVersion, data.label)}>
                Edit from this version
              </Button>
              {selected.live ? null : (
                <Button size="sm" variant="primary" onClick={() => setConfirming(true)}>
                  {action}
                </Button>
              )}
            </div>
          </div>
          {selected.commitMessage ? <p className="text-small text-fg-secondary">{selected.commitMessage}</p> : null}
          {selected.live ? (
            <p className="text-small text-fg-muted">This version is live. Select another version to compare it.</p>
          ) : live ? (
            <LineDiff before={promptText(live)} after={promptText(selected)} />
          ) : (
            <p className="text-small text-fg-muted">No version is live, so there is nothing to compare with.</p>
          )}
        </section>
      </div>

      <ConfirmDialog
        open={confirming}
        title={rollBack ? `Roll back to version ${selected.version}?` : `Publish version ${selected.version}?`}
        tone="primary"
        confirmLabel={rollBack ? 'Roll back' : 'Publish'}
        busy={publish.isPending}
        error={publish.isError ? <ErrorMessage error={publish.error} title="Could not move the label" /> : null}
        message={
          <p>
            The <strong className="font-medium text-fg">{data.label}</strong> label moves {live ? `from version ${live.version} ` : ''}to version {selected.version}, and new answers use it at once.
            {live ? ` You can move it back the same way.` : ''}
          </p>
        }
        onConfirm={confirm}
        onCancel={() => {
          setConfirming(false);
          publish.reset();
        }}
      />
    </div>
  );
}
