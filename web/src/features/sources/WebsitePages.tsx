import { ChevronLeft, ChevronRight, ExternalLink, MonitorX } from 'lucide-react';
import { useState } from 'react';
import { Button } from '../../components/ui/Button';
import { Alert, EmptyState, ErrorMessage, Spinner } from '../../components/ui/feedback';
import { Table, TableCell, TableHead, TableHeader, TableRow, TableRowHeader } from '../../components/ui/Table';
import { formatDateTime, formatNumber, formatRelativeTime, pluralize, safeExternalUrl } from '../../lib/format';
import { useWebsitePages, WEBSITE_PAGES_LIMIT } from './api';
import { anyIngesting } from './polling';
import { SourceStatusBadge } from './SourceStatusBadge';
import type { SourceDto } from './types';

export function isCrawling(website: Pick<SourceDto, 'status'>): boolean {
  return anyIngesting([website]);
}

function CrawlProgress({ website }: { website: SourceDto }) {
  const crawl = website.crawl;
  if (!crawl) {
    return null;
  }
  const parts = [
    pluralize(crawl.pagesFound, 'page') + ' found',
    `${formatNumber(crawl.pagesDone)} done`,
    crawl.pagesFailed > 0 ? `${formatNumber(crawl.pagesFailed)} failed` : null,
  ].filter(Boolean);
  return (
    <p className="text-xs text-fg-muted tabular-nums" aria-live="polite">
      {isCrawling(website) ? 'Crawling: ' : 'Last crawl: '}
      {parts.join(' · ')}
    </p>
  );
}

function NeedsBrowserNotice({ count }: { count: number }) {
  return (
    <Alert tone="warning" icon={MonitorX} title={`${pluralize(count, 'page')} ${count === 1 ? 'needs' : 'need'} a browser`}>
      These pages show almost no text until JavaScript runs, so there was nothing to index. Add their content as a file or as text instead, or point
      the website at pages that work without JavaScript.
    </Alert>
  );
}

function PageRow({ page }: { page: SourceDto }) {
  const url = safeExternalUrl(page.url);
  const fetched = page.lastFetchedAt ?? page.processedAt;
  return (
    <TableRow>
      <TableRowHeader className="w-full max-w-0">
        <div className="flex min-w-0 items-center gap-1">
          <span className="min-w-0 truncate" title={page.url ?? undefined}>
            {page.title || url || 'Untitled'}
          </span>
          {url ? (
            <a
              href={url}
              target="_blank"
              rel="noopener noreferrer"
              aria-label={`Open “${page.title || url}” in a new tab`}
              title={url}
              className="flex size-5 shrink-0 items-center justify-center rounded-sm text-fg-muted hover:text-fg"
            >
              <ExternalLink className="size-3" aria-hidden />
            </a>
          ) : null}
        </div>
        {page.status === 'failed' && page.error ? <p className="mt-0.5 truncate text-xs font-normal text-danger-text">{page.error}</p> : null}
      </TableRowHeader>
      <TableCell>
        <SourceStatusBadge source={page} />
      </TableCell>
      <TableCell className="whitespace-nowrap" title={fetched ? formatDateTime(fetched) : undefined}>
        {fetched ? formatRelativeTime(fetched) : '–'}
      </TableCell>
    </TableRow>
  );
}

/** The pages a website's crawl found, with their status, kept current while it crawls. */
export function WebsitePages({ website }: { website: SourceDto }) {
  const [offset, setOffset] = useState(0);
  const crawling = isCrawling(website);
  const pages = useWebsitePages(website.id, crawling, offset);
  const items = pages.data?.items ?? [];
  const total = pages.data?.total ?? 0;
  const needsBrowser = items.filter((page) => page.status === 'needsbrowser').length;

  return (
    <section aria-labelledby="website-pages-heading" className="space-y-3">
      <div className="flex items-baseline justify-between gap-2">
        <h3 id="website-pages-heading" className="text-sm font-semibold text-fg">
          Pages
        </h3>
        <CrawlProgress website={website} />
      </div>
      {needsBrowser > 0 ? <NeedsBrowserNotice count={needsBrowser} /> : null}
      {pages.isPending ? (
        <div className="py-8 text-center">
          <Spinner label="Loading pages" />
        </div>
      ) : pages.isError ? (
        <ErrorMessage error={pages.error} />
      ) : items.length === 0 ? (
        <EmptyState
          title={crawling ? 'Looking for pages' : 'No pages yet'}
          description={crawling ? 'Pages show up here as they are fetched.' : 'The crawl found no pages it could index.'}
          className="py-6"
        />
      ) : (
        <div className="overflow-hidden rounded-xl border border-border">
          <Table aria-label="Pages of the website" aria-busy={pages.isFetching}>
            <TableHead>
              <TableHeader>Page</TableHeader>
              <TableHeader>Status</TableHeader>
              <TableHeader>Last fetched</TableHeader>
            </TableHead>
            <tbody>
              {items.map((page) => (
                <PageRow key={page.id} page={page} />
              ))}
            </tbody>
          </Table>
        </div>
      )}
      {total > WEBSITE_PAGES_LIMIT ? (
        <div className="flex items-center justify-end gap-1.5">
          <Button
            size="sm"
            icon={ChevronLeft}
            disabled={offset === 0 || pages.isFetching}
            onClick={() => setOffset((value) => Math.max(0, value - WEBSITE_PAGES_LIMIT))}
          >
            Previous
          </Button>
          <span className="px-1 text-xs text-fg-muted tabular-nums">
            {formatNumber(offset + 1)}–{formatNumber(Math.min(total, offset + WEBSITE_PAGES_LIMIT))} of {formatNumber(total)}
          </span>
          <Button
            size="sm"
            disabled={offset + WEBSITE_PAGES_LIMIT >= total || pages.isFetching}
            onClick={() => setOffset((value) => value + WEBSITE_PAGES_LIMIT)}
          >
            Next
            <ChevronRight className="size-3.5" aria-hidden />
          </Button>
        </div>
      ) : null}
    </section>
  );
}
