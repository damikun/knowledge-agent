import { clsx } from 'clsx';
import { ChevronDown, Library, Search, X } from 'lucide-react';
import { useId, useMemo, useRef, useState } from 'react';
import { Link } from 'react-router';
import { Button } from '../../components/ui/Button';
import { Spinner } from '../../components/ui/feedback';
import { Popover } from '../../components/ui/Popover';
import { errorMessage } from '../../lib/api';
import { formatNumber, shortId } from '../../lib/format';
import { useDebouncedSearch } from '../../lib/hooks/useDebouncedSearch';
import { useSourcesPage } from './api';
import type { SourceDto } from './types';
import { useSourceTitles } from './useSourceTitles';
import { useReturnState } from '../../lib/returnTo';

const SCOPE_RESULT_LIMIT = 50;

export interface SourceScopePickerProps {
  /** Empty means all sources. */
  value: string[];
  onChange: (value: string[]) => void;
  disabled?: boolean;
}

export function SourceScopePicker({ value, onChange, disabled = false }: SourceScopePickerProps) {
  const returnState = useReturnState();
  const [open, setOpen] = useState(false);
  const search = useDebouncedSearch();
  const buttonRef = useRef<HTMLButtonElement>(null);
  const searchId = useId();
  const selected = useMemo(() => new Set(value), [value]);
  // Remembered so the chips keep their labels when a later search no longer lists the source.
  const [pickedTitles, setPickedTitles] = useState<Readonly<Record<string, string>>>({});
  // Ids selected elsewhere (e.g. by the parent) are looked up by id.
  const fetchedTitle = useSourceTitles(value.filter((id) => pickedTitles[id] === undefined));
  const titleOf = (id: string): string | undefined => pickedTitles[id] ?? fetchedTitle(id);

  const results = useSourcesPage({ status: ['ready'], query: search.query, limit: SCOPE_RESULT_LIMIT }, { enabled: open });
  const items = results.data?.items ?? [];
  const total = results.data?.total ?? 0;

  const toggle = (source: SourceDto) => {
    if (selected.has(source.id)) {
      onChange(value.filter((id) => id !== source.id));
      return;
    }
    setPickedTitles((current) => ({ ...current, [source.id]: source.title || 'Untitled' }));
    onChange([...value, source.id]);
  };

  const first = value[0];
  const label =
    value.length === 0 ? 'All sources' : value.length === 1 && first !== undefined ? (titleOf(first) ?? '1 source') : `${formatNumber(value.length)} sources`;

  return (
    <>
      <button
        ref={buttonRef}
        type="button"
        disabled={disabled}
        onClick={() => setOpen((v) => !v)}
        aria-expanded={open}
        aria-haspopup="dialog"
        aria-label={`Source scope: ${label}`}
        title={value.length > 0 ? value.map((id) => titleOf(id) ?? id).join(', ') : undefined}
        // A chosen scope reads as a selection, so it is easy to tell that answers are limited.
        className={clsx(
          'inline-flex h-7 min-w-0 items-center gap-1.5 rounded-md px-2 text-small font-medium transition-colors duration-100 ease-out disabled:opacity-50',
          value.length > 0 ? 'bg-fill-active text-fg' : 'text-fg-secondary hover:bg-fill-hover hover:text-fg',
        )}
      >
        <Library className="size-3.5 shrink-0" aria-hidden />
        <span className="max-w-56 truncate">{label}</span>
        <ChevronDown className="size-3.5 shrink-0 opacity-70" aria-hidden />
      </button>
      <Popover open={open} onClose={() => setOpen(false)} anchorRef={buttonRef} label="Choose sources" className="w-80">
        <div className="border-b border-border-subtle p-3">
          <div className="text-small font-medium text-fg">Limit answers to</div>
          <p className="text-xs text-fg-muted">Leave empty to search every ready source.</p>
          <div className="relative mt-2">
            <Search className="pointer-events-none absolute top-1/2 left-2.5 size-3.5 -translate-y-1/2 text-fg-muted" aria-hidden />
            <label htmlFor={searchId} className="sr-only">
              Search ready sources
            </label>
            <input
              id={searchId}
              type="search"
              value={search.input}
              onChange={(event) => search.setInput(event.target.value)}
              onKeyDown={(event) => {
                if (event.key === 'Enter') {
                  event.preventDefault();
                  search.flush();
                }
              }}
              placeholder="Search title, URL or file"
              className="field h-7 py-1 pl-7 text-small"
            />
          </div>
          {value.length > 0 ? (
            <ul className="mt-2 flex max-h-24 flex-wrap gap-1 overflow-y-auto" aria-label="Selected sources">
              {value.map((id) => {
                const title = titleOf(id) ?? shortId(id);
                return (
                  <li key={id} className="max-w-full">
                    <span className="inline-flex h-6 max-w-full items-center gap-0.5 rounded-full bg-fill-muted pr-0.5 pl-2.5 text-xs font-medium text-fg-secondary">
                      <span className="truncate" title={title}>
                        {title}
                      </span>
                      <button
                        type="button"
                        onClick={() => onChange(value.filter((v) => v !== id))}
                        aria-label={`Remove ${title}`}
                        className="flex size-5 shrink-0 items-center justify-center rounded-full text-fg-muted hover:bg-fill-hover hover:text-fg"
                      >
                        <X className="size-3" aria-hidden />
                      </button>
                    </span>
                  </li>
                );
              })}
            </ul>
          ) : null}
        </div>
        {results.isPending ? (
          <div className="p-3">
            <Spinner label="Loading sources" className="size-3.5" />
          </div>
        ) : results.isError ? (
          <p className="p-3 text-xs text-danger-text" role="alert">
            {errorMessage(results.error)}
          </p>
        ) : items.length === 0 ? (
          search.query ? (
            <p className="p-3 text-xs text-fg-muted">No ready sources match “{search.query}”.</p>
          ) : (
            <p className="p-3 text-xs text-fg-muted">
              No ready sources yet.{' '}
              <Link to="/sources" state={returnState} className="font-medium text-fg underline underline-offset-2">
                Add sources
              </Link>
            </p>
          )
        ) : (
          <>
            <ul className="max-h-64 overflow-y-auto p-1" aria-label="Sources" aria-busy={results.isPlaceholderData || undefined}>
              {items.map((source) => (
                <li key={source.id}>
                  <label className="flex cursor-pointer items-start gap-2.5 rounded-md px-2 py-1.5 text-small hover:bg-fill-hover">
                    <input type="checkbox" className="mt-0.5 size-4 shrink-0" checked={selected.has(source.id)} onChange={() => toggle(source)} />
                    <span className="min-w-0">
                      <span className="block truncate font-medium text-fg">{source.title || 'Untitled'}</span>
                      <span className="block truncate text-xs text-fg-muted">
                        {source.type} · {formatNumber(source.chunkCount)} chunks
                      </span>
                    </span>
                  </label>
                </li>
              ))}
            </ul>
            {total > items.length ? (
              <p className="border-t border-border-subtle px-3 py-2 text-xs text-fg-muted">
                Showing {formatNumber(items.length)} of {formatNumber(total)} ready sources. Search to find others.
              </p>
            ) : null}
          </>
        )}
        {value.length > 0 ? (
          <div className="flex justify-between gap-2 border-t border-border-subtle p-2">
            <Button size="sm" variant="ghost" onClick={() => onChange([])}>
              Clear selection
            </Button>
            <Button size="sm" variant="primary" onClick={() => setOpen(false)}>
              Done
            </Button>
          </div>
        ) : null}
      </Popover>
    </>
  );
}
