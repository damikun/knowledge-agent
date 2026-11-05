import { CircleCheck, CircleX, Hourglass, LoaderCircle, MonitorX } from 'lucide-react';
import { Badge, type BadgeTone } from '../../components/ui/Badge';
import { clsx } from 'clsx';
import { formatNumber } from '../../lib/format';
import type { IngestionStage, SourceDto, SourceStatus } from './types';

const STATUS: Record<SourceStatus, { label: string; tone: BadgeTone; description: string }> = {
  pending: { label: 'Pending', tone: 'neutral', description: 'Waiting in the ingestion queue' },
  processing: { label: 'Processing', tone: 'accent', description: 'Extracting, chunking and embedding' },
  ready: { label: 'Ready', tone: 'success', description: 'Searchable' },
  failed: { label: 'Failed', tone: 'danger', description: 'Ingestion failed' },
  waiting: { label: 'Waiting for a model', tone: 'warning', description: 'Waiting for an embedding model' },
  needsbrowser: {
    label: 'Needs a browser',
    tone: 'warning',
    description: 'The page shows almost no text without JavaScript, so nothing was indexed',
  },
};

const TEXT: Record<SourceStatus, string> = {
  pending: 'text-fg-secondary',
  processing: 'text-fg',
  ready: 'text-fg-secondary',
  failed: 'text-danger-text',
  waiting: 'text-warning-text',
  needsbrowser: 'text-warning-text',
};

const STAGES: Record<IngestionStage, { label: string; description: string }> = {
  fetching: { label: 'Fetching', description: 'Downloading the page' },
  extracting: { label: 'Extracting', description: 'Extracting the text' },
  chunking: { label: 'Chunking', description: 'Splitting the text into chunks' },
  contextualizing: { label: 'Adding context', description: 'Placing each chunk in its document for search' },
  embedding: { label: 'Embedding', description: 'Computing a vector for each chunk' },
  storing: { label: 'Storing', description: 'Saving the chunks and vectors' },
};

function normalizeStatus(status: string | null | undefined): SourceStatus | null {
  const value = status?.toLowerCase();
  return (Object.keys(STATUS) as SourceStatus[]).find((known) => known === value) ?? null;
}

function normalizeStage(stage: string | null | undefined): IngestionStage | null {
  const value = stage?.toLowerCase();
  return (Object.keys(STAGES) as IngestionStage[]).find((known) => known === value) ?? null;
}

export interface SourceStatusBadgeProps {
  source: Pick<SourceDto, 'status' | 'error' | 'stage' | 'embeddedChunks' | 'chunkCount' | 'crawl'>;
}

// A website's crawl counts pages instead of chunks.
function progressOf(source: SourceStatusBadgeProps['source'], normalized: SourceStatus, stage: IngestionStage | null) {
  if (normalized === 'processing' && source.crawl) {
    const total = Math.max(1, source.crawl.pagesFound);
    const done = Math.min(total, Math.max(0, source.crawl.pagesDone + source.crawl.pagesFailed));
    return { label: `Crawling ${formatNumber(done)} of ${formatNumber(total)} pages`, done, total, name: 'Pages crawled' };
  }
  const total = Math.max(0, source.chunkCount);
  if (stage === 'embedding' && total > 0) {
    const done = Math.min(total, Math.max(0, source.embeddedChunks));
    return { label: `Embedding ${formatNumber(done)} of ${formatNumber(total)}`, done, total, name: 'Chunks embedded' };
  }
  return null;
}

export function SourceStatusBadge({ source }: SourceStatusBadgeProps) {
  const normalized = normalizeStatus(source.status);
  if (!normalized) {
    return <Badge tone="outline">{source.status || 'Unknown'}</Badge>;
  }
  const info = STATUS[normalized];
  const stage = normalized === 'processing' ? normalizeStage(source.stage) : null;
  const progress = progressOf(source, normalized, stage);
  const label = progress ? progress.label : stage ? STAGES[stage].label : info.label;
  const animated = normalized === 'pending' || normalized === 'processing';
  const reason = (normalized === 'failed' || normalized === 'waiting' || normalized === 'needsbrowser') && source.error ? source.error : null;
  // A colored icon and plain text: a column of pills competes with the titles, and Ready is the normal state.
  const badge = (
    <span
      title={
        source.crawl && normalized === 'processing'
          ? 'Fetching and indexing the pages of the website'
          : stage
            ? STAGES[stage].description
            : (reason ?? info.description)
      }
      data-status={normalized}
      data-tone={info.tone}
      data-animated={animated || undefined}
      className={clsx('inline-flex items-center gap-1.5 text-small whitespace-nowrap', TEXT[normalized])}
    >
      {normalized === 'pending' ? (
        <span className="mx-[3px] size-1.5 animate-pulse rounded-full bg-fg-muted" aria-hidden />
      ) : normalized === 'processing' ? (
        <LoaderCircle className="size-3.5 animate-spin text-accent-icon" aria-hidden />
      ) : normalized === 'ready' ? (
        <CircleCheck className="size-3.5 text-success" aria-hidden />
      ) : normalized === 'waiting' ? (
        <Hourglass className="size-3.5 text-warning" aria-hidden />
      ) : normalized === 'needsbrowser' ? (
        <MonitorX className="size-3.5 text-warning" aria-hidden />
      ) : (
        <CircleX className="size-3.5 text-danger" aria-hidden />
      )}
      {label}
      {/* Tooltips do not reach screen readers, and the table spells out only the errors of failed sources. */}
      {(normalized === 'waiting' || normalized === 'needsbrowser') && reason ? <span className="sr-only">: {reason}</span> : null}
    </span>
  );
  if (!progress) {
    return badge;
  }
  return (
    <span className="inline-flex flex-col gap-1">
      {badge}
      <span
        role="progressbar"
        aria-label={progress.name}
        aria-valuemin={0}
        aria-valuemax={progress.total}
        aria-valuenow={progress.done}
        className="relative h-1 overflow-hidden rounded-full bg-fill-muted"
      >
        <span
          className="absolute inset-y-0 left-0 rounded-full bg-accent transition-[width]"
          style={{ width: `${(progress.done / progress.total) * 100}%` }}
        />
      </span>
    </span>
  );
}
