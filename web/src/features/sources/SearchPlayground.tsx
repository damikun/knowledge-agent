import { useMutation } from '@tanstack/react-query';
import { clsx } from 'clsx';
import { ExternalLink, Search } from 'lucide-react';
import { useState, type ReactNode, type RefObject } from 'react';
import { Badge } from '../../components/ui/Badge';
import { Button } from '../../components/ui/Button';
import { EmptyState, ErrorMessage } from '../../components/ui/feedback';
import { Select } from '../../components/ui/Select';
import { hostname, safeExternalUrl } from '../../lib/format';
import { sourcesApi } from './api';
import { SourceScopePicker } from './SourceScopePicker';
import type { SearchHitDto, SearchMode, SearchRequest } from './types';

const TOP_K = (['3', '5', '10', '20'] as const).map((value) => ({ value, label: value }));

const MODES: ReadonlyArray<{ value: SearchMode | ''; label: string }> = [
  { value: '', label: 'Default' },
  { value: 'hybrid', label: 'Hybrid' },
  { value: 'vector', label: 'Vector' },
  { value: 'keyword', label: 'Keyword' },
];

type RerankChoice = '' | 'on' | 'off';

const RERANK_CHOICES: ReadonlyArray<{ value: RerankChoice; label: string }> = [
  { value: '', label: 'Default' },
  { value: 'on', label: 'On' },
  { value: 'off', label: 'Off' },
];

export interface HitSignal {
  label: string;
  description: string;
}

export function hitSignals(hit: SearchHitDto): HitSignal[] {
  const signals: HitSignal[] = [];
  if (hit.vectorRank != null) {
    signals.push({
      label: `Vector #${hit.vectorRank}${hit.vectorScore != null ? ` · ${hit.vectorScore.toFixed(3)}` : ''}`,
      description: 'Rank and cosine similarity in the vector results',
    });
  }
  if (hit.keywordRank != null) {
    signals.push({
      label: `Keyword #${hit.keywordRank}${hit.keywordScore != null ? ` · ${hit.keywordScore.toFixed(2)}` : ''}`,
      description: 'Rank and BM25 score in the keyword results',
    });
  }
  if (hit.rerankScore != null) {
    signals.push({ label: `Reranked · ${hit.rerankScore.toFixed(3)}`, description: 'Relevance from the reranker; it orders the results' });
  }
  return signals;
}

function ScoreBar({ score }: { score: number }) {
  const clamped = Math.min(1, Math.max(0, Number.isFinite(score) ? score : 0));
  return (
    <span className="inline-flex items-center gap-2">
      <span
        role="meter"
        aria-label="Relevance score"
        aria-valuemin={0}
        aria-valuemax={1}
        aria-valuenow={Number(clamped.toFixed(3))}
        className="relative h-1 w-16 overflow-hidden rounded-full bg-fill-muted"
      >
        <span className="absolute inset-y-0 left-0 rounded-full bg-accent" style={{ width: `${clamped * 100}%` }} />
      </span>
      <span className="text-xs text-fg-muted tabular-nums">{score.toFixed(3)}</span>
    </span>
  );
}

function Hit({ hit, rank }: { hit: SearchHitDto; rank: number }) {
  const [expanded, setExpanded] = useState(false);
  const url = safeExternalUrl(hit.sourceUrl);
  const long = hit.content.length > 420;
  const signals = hitSignals(hit);
  return (
    <li className="rounded-xl border border-border px-4 py-3">
      <div className="flex flex-wrap items-center gap-x-3 gap-y-1">
        <span className="text-xs font-medium text-fg-muted tabular-nums">{rank}.</span>
        <span className="min-w-0 flex-1 basis-40 truncate text-sm font-medium text-fg">
          {url ? (
            <a href={url} target="_blank" rel="noopener noreferrer" className="inline-flex items-center gap-1 hover:underline">
              {hit.sourceTitle || hostname(url)}
              <ExternalLink className="size-3 text-fg-muted" aria-hidden />
            </a>
          ) : (
            hit.sourceTitle || 'Untitled source'
          )}
        </span>
        {hit.page != null ? <Badge>Page {hit.page}</Badge> : null}
        <Badge>Chunk #{hit.chunkIndex}</Badge>
        <ScoreBar score={hit.score} />
      </div>
      {signals.length > 0 ? (
        <ul className="mt-1.5 flex flex-wrap gap-1.5" aria-label="Retrieval details">
          {signals.map((signal) => (
            <li key={signal.label}>
              <Badge title={signal.description}>
                {signal.label}
                <span className="sr-only"> ({signal.description})</span>
              </Badge>
            </li>
          ))}
        </ul>
      ) : null}
      <p className={clsx('mt-2 text-sm leading-relaxed break-words whitespace-pre-wrap text-fg-secondary', !expanded && long && 'line-clamp-4')}>
        {hit.content}
      </p>
      {long ? (
        <button type="button" onClick={() => setExpanded((v) => !v)} className="mt-1 rounded-sm text-xs font-medium text-fg underline-offset-2 hover:underline">
          {expanded ? 'Show less' : 'Show more'}
        </button>
      ) : null}
    </li>
  );
}

function OptionField({ id, label, children }: { id: string; label: string; children: ReactNode }) {
  return (
    <div>
      <label htmlFor={id} className="mb-1 block text-xs font-medium text-fg-secondary">
        {label}
      </label>
      {children}
    </div>
  );
}

/** Test retrieval as a visitor would ask; the Sources page shows it in a side sheet titled "Test search". */
export function SearchPlayground({ queryRef }: { queryRef?: RefObject<HTMLInputElement | null> } = {}) {
  const [query, setQuery] = useState('');
  const [topK, setTopK] = useState(5);
  const [minScore, setMinScore] = useState('');
  const [scope, setScope] = useState<string[]>([]);
  const [mode, setMode] = useState<SearchMode | ''>('');
  const [rerank, setRerank] = useState<RerankChoice>('');
  const search = useMutation({ mutationFn: (request: SearchRequest) => sourcesApi.search(request) });

  const minScoreValue = minScore.trim() === '' ? null : Number(minScore);
  const minScoreInvalid = minScoreValue !== null && (Number.isNaN(minScoreValue) || minScoreValue < -1 || minScoreValue > 1);

  return (
    <div className="space-y-5">
      <form
        className="space-y-3"
        onSubmit={(event) => {
          event.preventDefault();
          if (query.trim() && !minScoreInvalid) {
            search.mutate({
              query: query.trim(),
              topK,
              minScore: minScoreValue,
              sourceIds: scope.length > 0 ? scope : null,
              mode: mode || null,
              rerank: rerank === '' ? null : rerank === 'on',
            });
          }
        }}
      >
        <div className="flex gap-2">
          <div className="min-w-0 flex-1">
            <label htmlFor="search-query" className="sr-only">
              Search query
            </label>
            <div className="relative">
              <Search className="pointer-events-none absolute top-1/2 left-2.5 size-4 -translate-y-1/2 text-fg-muted" aria-hidden />
              <input
                ref={queryRef}
                id="search-query"
                type="search"
                value={query}
                onChange={(event) => setQuery(event.target.value)}
                placeholder="What would you ask the agent?"
                className="field pl-8"
              />
            </div>
          </div>
          <Button type="submit" variant="primary" loading={search.isPending} disabled={!query.trim() || minScoreInvalid}>
            Search
          </Button>
        </div>
        <div className="grid grid-cols-2 gap-2 sm:grid-cols-4">
          <OptionField id="search-topk" label="Results">
            <Select id="search-topk" options={TOP_K} value={String(topK) as (typeof TOP_K)[number]['value']} onChange={(value) => setTopK(Number(value))} />
          </OptionField>
          <OptionField id="search-mode" label="Mode">
            <Select id="search-mode" options={MODES} value={mode} onChange={setMode} />
          </OptionField>
          <OptionField id="search-rerank" label="Rerank">
            <Select id="search-rerank" options={RERANK_CHOICES} value={rerank} onChange={setRerank} />
          </OptionField>
          <OptionField id="search-minscore" label="Min score">
            <input
              id="search-minscore"
              inputMode="decimal"
              value={minScore}
              onChange={(event) => setMinScore(event.target.value)}
              placeholder="0.0"
              aria-invalid={minScoreInvalid || undefined}
              className="field"
            />
          </OptionField>
        </div>
        <div className="flex items-center gap-2 text-small text-fg-muted">
          <span>Search in</span>
          <SourceScopePicker value={scope} onChange={setScope} />
        </div>
      </form>
      <div className="empty:hidden">
        {search.isError ? <ErrorMessage error={search.error} /> : null}
        {search.isSuccess ? (
          search.data.length === 0 ? (
            <EmptyState icon={Search} title="No matching chunks" description="Try another wording, lower the minimum score, or check that sources are ready." className="py-6" />
          ) : (
            <ol className="space-y-2" aria-label="Search results">
              {search.data.map((hit, index) => (
                <Hit key={hit.chunkId} hit={hit} rank={index + 1} />
              ))}
            </ol>
          )
        ) : null}
      </div>
    </div>
  );
}
