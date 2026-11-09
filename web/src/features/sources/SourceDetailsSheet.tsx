import { ChevronLeft, ChevronRight, ExternalLink, TriangleAlert } from 'lucide-react';
import { useState, type ReactNode } from 'react';
import { Badge } from '../../components/ui/Badge';
import { Button } from '../../components/ui/Button';
import { EmptyState, ErrorMessage, Spinner } from '../../components/ui/feedback';
import { SideSheet } from '../../components/ui/SideSheet';
import { formatBytes, formatDateTime, formatNumber, safeExternalUrl } from '../../lib/format';
import { useChunks } from './api';
import { SourceStatusBadge } from './SourceStatusBadge';
import { SuspiciousBadge, suspiciousChunks, suspiciousText } from './SuspiciousContent';
import { SOURCE_TYPES } from './sourceTypes';
import type { CrawlScope, SourceDto } from './types';
import { WebsitePages } from './WebsitePages';

const CHUNK_PAGE_SIZE = 20;

export interface SourceDetailsSheetProps {
  source: SourceDto | null;
  /** The folder path, when the source is in one. */
  folder: string | null;
  /** Its vectors come from another model than the current embedding model. */
  stale: boolean;
  onClose: () => void;
}

function Detail({ label, children, wide = false }: { label: string; children: ReactNode; wide?: boolean }) {
  return (
    <div className={wide ? 'col-span-2' : undefined}>
      <dt className="text-xs text-fg-muted">{label}</dt>
      <dd className="mt-0.5 min-w-0 text-small break-words text-fg">{children}</dd>
    </div>
  );
}

function SourceSizes({ source }: { source: SourceDto }) {
  return (
    <>
      <Detail label="Chunks">{formatNumber(source.chunkCount)}</Detail>
      <Detail label="Pages">{source.pageCount != null ? formatNumber(source.pageCount) : '–'}</Detail>
      <Detail label="Tokens">{formatNumber(source.tokenEstimate)}</Detail>
      {source.sizeBytes != null ? <Detail label="Size">{formatBytes(source.sizeBytes)}</Detail> : null}
      {source.lastFetchedAt ? <Detail label="Last fetched">{formatDateTime(source.lastFetchedAt)}</Detail> : null}
    </>
  );
}

function Chunks({ source }: { source: SourceDto }) {
  const [page, setPage] = useState(0);
  const skip = page * CHUNK_PAGE_SIZE;
  const chunks = useChunks(source.id, skip, CHUNK_PAGE_SIZE);
  const items = chunks.data ?? [];
  const total = source.chunkCount;
  const hasNext = total > 0 ? skip + CHUNK_PAGE_SIZE < total : items.length === CHUNK_PAGE_SIZE;
  const pageCount = total > 0 ? Math.max(1, Math.ceil(total / CHUNK_PAGE_SIZE)) : null;

  return (
    <section aria-labelledby="source-chunks-heading" className="space-y-3">
      <div className="flex items-center justify-between gap-2">
        <h3 id="source-chunks-heading" className="text-sm font-semibold text-fg">
          Chunks
        </h3>
        <span className="text-xs text-fg-muted tabular-nums" aria-live="polite">
          {items.length > 0
            ? `Showing ${formatNumber(skip + 1)}–${formatNumber(skip + items.length)}${total ? ` of ${formatNumber(total)}` : ''}`
            : ''}
        </span>
      </div>
      {chunks.isPending ? (
        <div className="py-8 text-center">
          <Spinner label="Loading chunks" />
        </div>
      ) : chunks.isError ? (
        <ErrorMessage error={chunks.error} />
      ) : items.length === 0 ? (
        <EmptyState title="No chunks stored" description="The source has not been ingested yet, or ingestion produced no text." className="py-6" />
      ) : (
        <ol className="space-y-3" aria-label="Chunks" aria-busy={chunks.isFetching}>
          {items.map((chunk) => (
            <li key={chunk.chunkId} className="overflow-hidden rounded-xl border border-border">
              <div className="flex items-center gap-2 border-b border-border-subtle bg-surface-subtle px-3 py-1.5 text-xs text-fg-muted">
                <span className="font-medium text-fg-secondary tabular-nums">#{chunk.chunkIndex}</span>
                {chunk.page != null ? <Badge>Page {chunk.page}</Badge> : null}
                <span className="ml-auto tabular-nums">{formatNumber(chunk.content.length)} chars</span>
              </div>
              <p className="max-h-64 overflow-y-auto px-3 py-2 text-sm leading-relaxed break-words whitespace-pre-wrap text-fg-secondary">
                {chunk.content}
              </p>
            </li>
          ))}
        </ol>
      )}
      {page > 0 || hasNext ? (
        <div className="flex items-center justify-end gap-1.5">
          <Button size="sm" icon={ChevronLeft} disabled={page === 0 || chunks.isFetching} onClick={() => setPage((p) => Math.max(0, p - 1))}>
            Previous
          </Button>
          {pageCount ? (
            <span className="px-1 text-xs text-fg-muted tabular-nums">
              {page + 1} / {pageCount}
            </span>
          ) : null}
          <Button size="sm" disabled={!hasNext || chunks.isFetching} onClick={() => setPage((p) => p + 1)}>
            Next
            <ChevronRight className="size-3.5" aria-hidden />
          </Button>
        </div>
      ) : null}
    </section>
  );
}

const SCOPE_LABELS: Record<CrawlScope, string> = {
  page: 'This page only',
  section: 'This page and the pages under it',
  site: 'The whole site',
};

function CrawlDetails({ source }: { source: SourceDto }) {
  const crawl = source.crawl;
  if (!crawl) {
    return null;
  }
  return (
    <>
      <Detail label="Crawls">{SCOPE_LABELS[crawl.scope] ?? crawl.scope}</Detail>
      <Detail label="Max pages">{formatNumber(crawl.maxPages)}</Detail>
      {crawl.exclude.length > 0 ? (
        <Detail label="Excluded" wide>
          <span className="font-mono text-xs">{crawl.exclude.join(', ')}</span>
        </Detail>
      ) : null}
    </>
  );
}

/** Everything about one source that the list leaves out, such as its size and embedding model, and its stored chunks or pages. */
export function SourceDetailsSheet({ source, folder, stale, onClose }: SourceDetailsSheetProps) {
  const url = safeExternalUrl(source?.url);
  const runUrl = safeExternalUrl(source?.jobRunUrl);
  const type = source ? (SOURCE_TYPES[source.type?.toLowerCase()]?.label ?? source.type) : '';
  const status = source?.status?.toLowerCase();
  const failed = status === 'failed' || status === 'needsbrowser';
  const website = source?.type === 'website';
  const suspicious = source ? suspiciousChunks(source) : 0;

  return (
    <SideSheet open={source !== null} onClose={onClose} size="lg" title={source?.title || source?.fileName || 'Untitled'} description={type}>
      {source ? (
        <div className="space-y-6">
          <dl className="grid grid-cols-2 gap-x-6 gap-y-4">
            <Detail label="Status">
              <SourceStatusBadge source={source} />
            </Detail>
            <Detail label="Added">{formatDateTime(source.createdAt)}</Detail>
            {suspicious > 0 ? (
              <Detail label="Content check" wide>
                <span className="flex flex-col items-start gap-1">
                  <SuspiciousBadge count={suspicious} />
                  <span className="text-small text-fg-secondary">{suspiciousText(suspicious)}</span>
                </span>
              </Detail>
            ) : null}
            {url ? (
              <Detail label="Address" wide>
                <a
                  href={url}
                  target="_blank"
                  rel="noopener noreferrer"
                  className="inline-flex max-w-full items-center gap-1 underline-offset-2 hover:underline"
                >
                  <span className="truncate">{url}</span>
                  <ExternalLink className="size-3 shrink-0 text-fg-muted" aria-hidden />
                </a>
              </Detail>
            ) : source.fileName ? (
              <Detail label="File" wide>
                {source.fileName}
              </Detail>
            ) : null}
            {folder ? <Detail label="Folder">{folder}</Detail> : null}
            {website ? <CrawlDetails source={source} /> : <SourceSizes source={source} />}
            {website ? null : (
              <Detail label="Embedding model" wide>
                {source.embeddingModel ? (
                  <span className="inline-flex max-w-full items-center gap-1.5 font-mono text-xs">
                    <span className="truncate">{source.embeddingModel}</span>
                    {stale ? (
                      <span className="inline-flex shrink-0 items-center gap-1 font-sans text-warning-text">
                        <TriangleAlert className="size-3.5" aria-hidden />
                        Not the current model
                      </span>
                    ) : null}
                  </span>
                ) : (
                  <span className="text-fg-muted">Not embedded yet</span>
                )}
              </Detail>
            )}
            {source.attempts > 1 || runUrl ? (
              <Detail label="Last run" wide>
                <span className="flex flex-wrap items-center gap-x-3">
                  {source.attempts > 1 ? <span>Attempt {source.attempts}</span> : null}
                  {runUrl ? (
                    <a
                      href={runUrl}
                      target="_blank"
                      rel="noopener noreferrer"
                      className="inline-flex items-center gap-1 underline-offset-2 hover:underline"
                    >
                      Open in the Inngest dashboard
                      <ExternalLink className="size-3 shrink-0 text-fg-muted" aria-hidden />
                    </a>
                  ) : null}
                </span>
              </Detail>
            ) : null}
            {failed && source.error ? (
              <Detail label={status === 'needsbrowser' ? 'Why' : 'Error'} wide>
                <span className={status === 'needsbrowser' ? 'text-warning-text' : 'text-danger-text'}>{source.error}</span>
              </Detail>
            ) : null}
          </dl>
          {/* Keyed by source, so another source starts at its first page. */}
          {website ? <WebsitePages key={source.id} website={source} /> : <Chunks key={source.id} source={source} />}
        </div>
      ) : null}
    </SideSheet>
  );
}
