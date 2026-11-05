import { clsx } from 'clsx';
import { FlaskConical, ScrollText } from 'lucide-react';
import { useId, useState } from 'react';
import { Link, Navigate, useParams, useSearchParams } from 'react-router';
import { EmptyState, ErrorMessage } from '../../components/ui/feedback';
import { Skeleton } from '../../components/ui/Skeleton';
import { TabPanel, Tabs, type TabItem } from '../../components/ui/Tabs';
import { Page } from '../../components/ui/Page';
import { useMediaQuery } from '../../lib/hooks/useMediaQuery';
import { usePrompts } from './api';
import { groupPrompts, promptInfo } from './catalog';
import { PromptDetail, type DetailTab } from './PromptDetail';
import { TemplatePlayground } from './TemplatePlayground';
import type { PromptSummaryDto } from './types';

type View = 'catalog' | 'playground';

const VIEWS: TabItem<View>[] = [
  { id: 'catalog', label: 'Prompts', icon: ScrollText },
  { id: 'playground', label: 'Playground', icon: FlaskConical },
];

function PromptGroup({ label, prompts, selected }: { label: string; prompts: PromptSummaryDto[]; selected: string | null }) {
  const labelId = useId();
  return (
    <div>
      <p id={labelId} className="mb-1 px-2.5 text-xs font-medium text-fg-muted">
        {label}
      </p>
      <ul aria-labelledby={labelId} className="space-y-0.5">
        {prompts.map((prompt) => {
          const active = prompt.name === selected;
          const info = promptInfo(prompt.name);
          return (
            <li key={prompt.name}>
              <Link
                to={`/prompts/${encodeURIComponent(prompt.name)}`}
                aria-current={active ? 'page' : undefined}
                className={clsx('block rounded-md px-2.5 py-2', active ? 'bg-fill-active' : 'hover:bg-fill-hover')}
              >
                <span className="flex items-baseline justify-between gap-2">
                  <span className={clsx('truncate text-sm', active ? 'font-medium text-fg' : 'text-fg-secondary')}>{info.title}</span>
                  <span className="shrink-0 text-xs text-fg-muted tabular-nums">v{prompt.version}</span>
                </span>
                <span className="mt-0.5 line-clamp-2 text-xs text-fg-muted">{info.description}</span>
              </Link>
            </li>
          );
        })}
      </ul>
    </div>
  );
}

function PromptList({ prompts, selected }: { prompts: PromptSummaryDto[]; selected: string | null }) {
  return (
    <nav aria-label="Prompts" className="space-y-4 pt-1">
      {groupPrompts(prompts).map((group) => (
        <PromptGroup key={group.id} label={group.label} prompts={group.prompts} selected={selected} />
      ))}
    </nav>
  );
}

function LoadingList() {
  return (
    <div role="status" className="space-y-2 px-2.5 pt-2">
      <span className="sr-only">Loading prompts</span>
      {[0.7, 0.55, 0.8, 0.6].map((width, index) => (
        <div key={index} className="space-y-1.5 py-1.5">
          <Skeleton className="h-3.5" style={{ width: `${width * 100}%` }} />
          <Skeleton className="h-2.5 w-full" />
        </div>
      ))}
    </div>
  );
}

// Below the shell's drawer breakpoint the list and the detail take turns.
const NARROW_QUERY = '(max-width: 767px)';

export function PromptsPage() {
  const { name: routeName } = useParams();
  const name = routeName ?? null;
  const [searchParams, setSearchParams] = useSearchParams();
  const view: View = searchParams.get('view') === 'playground' ? 'playground' : 'catalog';
  const prompts = usePrompts();
  const narrow = useMediaQuery(NARROW_QUERY);
  // Kept across prompts, so comparing previews stays on the preview tab.
  const [detailTab, setDetailTab] = useState<DetailTab>('template');

  // Select the first prompt so the page never opens empty; narrow windows show the list first instead.
  const first = prompts.data ? groupPrompts(prompts.data)[0]?.prompts[0]?.name : undefined;
  const firstPath = !name && first && view === 'catalog' && !narrow ? `/prompts/${encodeURIComponent(first)}` : null;

  const showList = !narrow || !name;
  const showDetail = !narrow || Boolean(name);

  return (
    <Page variant="workspace">
      {firstPath ? <Navigate to={firstPath} replace /> : null}
      <header className="flex min-h-12 shrink-0 items-end gap-4 border-b border-border-subtle px-4 lg:px-6">
        <div className="flex min-w-0 flex-1 items-baseline gap-3 py-2.5">
          <h1 className="shrink-0 text-title text-fg">Prompts</h1>
          <p className="hidden truncate text-small text-fg-muted lg:block">The instructions behind every answer. Edit, preview and publish them here.</p>
        </div>
        <Tabs
          idPrefix="prompts"
          items={VIEWS}
          value={view}
          onChange={(next) => setSearchParams(next === 'playground' ? { view: 'playground' } : {}, { replace: true })}
          label="Prompt views"
          className="-mb-px shrink-0"
        />
      </header>
      <TabPanel idPrefix="prompts" id={view} className="min-h-0 flex-1 rounded-none">
        {view === 'playground' ? (
          <div className="h-full overflow-y-auto">
            <div className="max-w-7xl px-4 py-6 lg:px-6">
              <TemplatePlayground />
            </div>
          </div>
        ) : (
          <div className="grid h-full grid-rows-[minmax(0,1fr)] md:grid-cols-[17rem_minmax(0,1fr)]">
            {showList ? (
              <div className="min-h-0 overflow-y-auto border-border-subtle p-2 md:border-r">
                {prompts.isPending ? (
                  <LoadingList />
                ) : prompts.isError ? (
                  <ErrorMessage error={prompts.error} />
                ) : prompts.data.length === 0 ? (
                  <EmptyState icon={ScrollText} title="No prompts" description="The API reported no prompts." className="py-6" />
                ) : (
                  <PromptList prompts={prompts.data} selected={name} />
                )}
              </div>
            ) : null}
            {showDetail ? (
              <div className="min-h-0 min-w-0">
                {name ? (
                  <PromptDetail name={name} tab={detailTab} onTabChange={setDetailTab} backTo={narrow ? '/prompts' : undefined} />
                ) : prompts.data && prompts.data.length === 0 ? null : (
                  <EmptyState icon={ScrollText} title="Select a prompt" description="Choose a prompt to see its template and a live preview." />
                )}
              </div>
            ) : null}
          </div>
        )}
      </TabPanel>
    </Page>
  );
}
