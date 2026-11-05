import { useMutation, useQueryClient } from '@tanstack/react-query';
import { clsx } from 'clsx';
import { RefreshCw } from 'lucide-react';
import { Button } from '../../components/ui/Button';
import { Alert, ErrorMessage } from '../../components/ui/feedback';
import { StatusDot } from '../../components/ui/StatusDot';
import { pluralize } from '../../lib/format';
import { sourceKeys, sourcesApi } from './api';
import { librarySummary } from './sourceFilters';
import type { SourceStatsDto } from './types';

export function ReindexNotice({ count, models, currentKey }: { count: number; models: string[]; currentKey: string }) {
  const queryClient = useQueryClient();
  const reindex = useMutation({ mutationFn: () => sourcesApi.reindexAll(), onSuccess: () => void queryClient.invalidateQueries({ queryKey: sourceKeys.all }) });
  if (reindex.isSuccess) {
    return (
      <Alert tone="accent" icon={RefreshCw}>
        Re-indexing started
        {reindex.data?.queued != null ? ` for ${pluralize(reindex.data.queued, 'source')}` : ''}. Progress shows in the list below.
      </Alert>
    );
  }
  return (
    <Alert
      tone="warning"
      title={`${pluralize(count, 'source')} embedded with a different model`}
      action={
        <Button variant="secondary" size="sm" icon={RefreshCw} loading={reindex.isPending} onClick={() => reindex.mutate()}>
          Re-index all
        </Button>
      }
    >
      <p>
        Stored vectors use <code className="font-mono text-xs">{models.join(', ')}</code>, but the current embedding model is{' '}
        <code className="font-mono text-xs">{currentKey}</code>. Searches only match chunks embedded with the current model.
      </p>
      {reindex.isError ? <ErrorMessage error={reindex.error} className="mt-2" /> : null}
    </Alert>
  );
}

/**
 * The state of the whole library, whichever folder is open. The status chips show the same counts, so this list is for
 * screen readers, which hear each change.
 */
export function LibrarySummary({ stats }: { stats: SourceStatsDto }) {
  return (
    <ul aria-label="Library" className="sr-only">
      {librarySummary(stats).map((item, index) => (
        <li key={item.label} className={clsx('flex items-center gap-1.5 tabular-nums', index === 0 && 'font-medium text-fg')}>
          {item.tone ? <StatusDot tone={item.tone} pulse={item.tone === 'info'} /> : null}
          {item.label}
        </li>
      ))}
    </ul>
  );
}
