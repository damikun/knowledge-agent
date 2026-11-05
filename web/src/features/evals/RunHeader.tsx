import { useMutation, useQueryClient } from '@tanstack/react-query';
import { CircleStop, ExternalLink, Trash } from 'lucide-react';
import { useState } from 'react';
import { useNavigate } from 'react-router';
import { Button, buttonClass } from '../../components/ui/Button';
import { ErrorMessage, PageHeader } from '../../components/ui/feedback';
import { Menu } from '../../components/ui/Menu';
import { ConfirmDialog } from '../../components/ui/Modal';
import { formatRelativeTime, safeExternalUrl } from '../../lib/format';
import { evalKeys, evalsApi } from './api';
import { DatasetSourceBadge } from './EvalBadges';
import { isRunActive } from './polling';
import type { EvalRunDto, EvalRunSummaryDto } from './types';

export function RunHeader({ run }: { run: EvalRunDto }) {
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const { summary } = run;
  const active = isRunActive(summary.status);
  const [confirmDelete, setConfirmDelete] = useState(false);
  const cancel = useMutation({
    mutationFn: () => evalsApi.cancelEvalRun(summary.id),
    onSuccess: (updated) => {
      if (updated) {
        queryClient.setQueryData<EvalRunDto>(evalKeys.run(summary.id), (old) => (old ? { ...old, summary: updated } : old));
      }
      void queryClient.invalidateQueries({ queryKey: evalKeys.runs });
    },
  });

  const remove = useMutation({
    mutationFn: () => evalsApi.deleteEvalRun(summary.id),
    onSuccess: () => {
      setConfirmDelete(false);
      navigate('/evals', { replace: true });
      queryClient.setQueriesData<EvalRunSummaryDto[]>({ queryKey: evalKeys.runLists }, (old) => old?.filter((r) => r.id !== summary.id));
      queryClient.removeQueries({ queryKey: evalKeys.run(summary.id), exact: true });
      void queryClient.invalidateQueries({ queryKey: evalKeys.runLists });
    },
  });

  const langfuseUrl = safeExternalUrl(summary.langfuseUrl);
  const name = summary.name || 'Untitled run';

  return (
    <>
      <PageHeader
        title={name}
        description={
          <span className="inline-flex flex-wrap items-center gap-x-1.5 gap-y-1">
            <span className="font-mono text-xs">{summary.dataset}</span>
            <DatasetSourceBadge source={summary.datasetSource} />
            <span>· created {formatRelativeTime(summary.createdAt)}</span>
          </span>
        }
        actions={
          <>
            {active ? null : (
              <Menu
                label="More actions for this run"
                items={[
                  {
                    label: 'Delete run',
                    icon: Trash,
                    danger: true,
                    onSelect: () => {
                      remove.reset();
                      setConfirmDelete(true);
                    },
                  },
                ]}
              />
            )}
            {langfuseUrl ? (
              <a href={langfuseUrl} target="_blank" rel="noopener noreferrer" className={buttonClass()}>
                Open in Langfuse
                <ExternalLink className="size-4" aria-hidden />
              </a>
            ) : null}
            {active ? (
              <Button icon={CircleStop} loading={cancel.isPending} onClick={() => cancel.mutate()}>
                Cancel run
              </Button>
            ) : null}
          </>
        }
      />

      {cancel.isError ? <ErrorMessage error={cancel.error} title="The run was not cancelled" /> : null}

      <ConfirmDialog
        open={confirmDelete}
        title="Delete run?"
        message={
          <>
            “{name}” and its results will be removed from the portal.
            {langfuseUrl ? ' The experiment recorded in Langfuse is kept.' : ''}
          </>
        }
        confirmLabel="Delete run"
        busy={remove.isPending}
        error={remove.isError ? <ErrorMessage error={remove.error} /> : null}
        onConfirm={() => remove.mutate()}
        onCancel={() => setConfirmDelete(false)}
      />
    </>
  );
}
