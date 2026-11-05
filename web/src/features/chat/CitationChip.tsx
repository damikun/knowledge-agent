import {
  autoUpdate,
  flip,
  FloatingFocusManager,
  FloatingPortal,
  offset,
  safePolygon,
  shift,
  useClick,
  useDismiss,
  useFloating,
  useFocus,
  useHover,
  useInteractions,
  useRole,
} from '@floating-ui/react';
import { ArrowUpRight, FileText, Globe, type LucideIcon } from 'lucide-react';
import { Fragment, useState } from 'react';
import { Link } from 'react-router';
import { citationMarkerClass } from '../../components/ui/CitationMarker';
import { hostname, safeExternalUrl } from '../../lib/format';
import { useReturnState } from '../../lib/returnTo';
import { pagesLabel, useAnswerSources, type AnswerSource } from './answerSources';
import { useChatDisplay } from './chatDisplay';
import { useCitation } from './citationContext';
import type { CitationDto } from './types';

/** Favicons would mean a third-party request for every source. */
export function sourceIcon(source: Pick<AnswerSource, 'url'>): LucideIcon {
  return source.url ? Globe : FileText;
}

export function sourcesPageLink(sourceId: string): string {
  return `/sources?source=${encodeURIComponent(sourceId)}`;
}

export function sourceOrigin(source: AnswerSource): string {
  const pages = pagesLabel(source.citations);
  return [source.host ?? 'File', pages].filter(Boolean).join(' · ');
}

function OpenSourceLink({ source, className, onNavigate }: { source: AnswerSource; className?: string; onNavigate?: () => void }) {
  const returnState = useReturnState();
  if (source.url) {
    return (
      <a href={source.url} target="_blank" rel="noopener noreferrer" className={className} onClick={onNavigate}>
        Open source
        <ArrowUpRight className="size-3.5" aria-hidden />
        <span className="sr-only"> (new tab)</span>
      </a>
    );
  }
  return (
    <Link to={sourcesPageLink(source.sourceId)} state={returnState} className={className} onClick={onNavigate}>
      Open source
      <ArrowUpRight className="size-3.5" aria-hidden />
    </Link>
  );
}

function SourceHoverCard({ source, citation, onNavigate }: { source: AnswerSource; citation: CitationDto; onNavigate: () => void }) {
  const { developer } = useChatDisplay();
  const Icon = sourceIcon(source);
  return (
    <div className="space-y-2">
      <div className="flex items-start gap-2">
        <span className="mt-0.5 flex size-5 shrink-0 items-center justify-center rounded-sm bg-fill-muted text-fg-muted">
          <Icon className="size-3.5" aria-hidden />
        </span>
        <div className="min-w-0">
          <div className="line-clamp-2 font-medium break-words text-fg">{source.title}</div>
          <div className="truncate text-xs text-fg-muted">
            {sourceOrigin(source)}
            {developer && Number.isFinite(citation.score) ? ` · score ${citation.score.toFixed(2)}` : ''}
          </div>
        </div>
      </div>
      {citation.snippet ? (
        <blockquote className="line-clamp-6 border-l-2 border-border pl-2.5 text-xs leading-relaxed break-words whitespace-pre-line text-fg-secondary">
          {citation.snippet}
        </blockquote>
      ) : null}
      <OpenSourceLink
        source={source}
        onNavigate={onNavigate}
        className="inline-flex items-center gap-1 text-xs font-medium text-accent-text hover:underline"
      />
    </div>
  );
}

/** Hover or focus shows the source card; a click or Enter pins it, for touch and for tabbing to its link. */
function CitationMarker({ source, citation }: { source: AnswerSource; citation: CitationDto }) {
  const [open, setOpen] = useState(false);
  const { refs, floatingStyles, context } = useFloating({
    open,
    onOpenChange: setOpen,
    placement: 'top-start',
    strategy: 'fixed',
    middleware: [offset(6), flip({ padding: 8 }), shift({ padding: 8 })],
    whileElementsMounted: autoUpdate,
  });
  const { getReferenceProps, getFloatingProps } = useInteractions([
    useHover(context, { delay: { open: 120, close: 80 }, handleClose: safePolygon() }),
    useFocus(context, { visibleOnly: true }),
    useClick(context, { ignoreMouse: true }),
    useDismiss(context),
    useRole(context, { role: 'dialog' }),
  ]);

  return (
    <>
      <button
        ref={refs.setReference}
        type="button"
        aria-label={`Source ${source.number}: ${source.title}${citation.page != null ? `, page ${citation.page}` : ''}`}
        className={citationMarkerClass({ tone: open ? 'open' : 'default', interactive: true })}
        {...getReferenceProps()}
      >
        {source.number}
      </button>
      {open ? (
        <FloatingPortal>
          <FloatingFocusManager context={context} modal={false} initialFocus={-1} returnFocus={false}>
            <div
              ref={refs.setFloating}
              style={floatingStyles}
              aria-label={`Source ${source.number}`}
              className="z-50 w-80 max-w-[calc(100vw-1rem)] animate-pop-in rounded-xl border border-border bg-raised p-3 text-sm text-fg shadow-menu outline-none"
              {...getFloatingProps()}
            >
              <SourceHoverCard source={source} citation={citation} onNavigate={() => setOpen(false)} />
            </div>
          </FloatingFocusManager>
        </FloatingPortal>
      ) : null}
    </>
  );
}

function UnknownMarker({ citationRef }: { citationRef: string }) {
  return (
    <span className={citationMarkerClass({ tone: 'unknown' })} title="This citation does not match a retrieved passage">
      ?<span className="sr-only">Unknown citation {citationRef}</span>
    </span>
  );
}

/** Passages of one source share its number, so a source shows once per group. */
export function CitationGroup({ refs }: { refs: readonly string[] }) {
  const { byRef } = useAnswerSources();
  const seen = new Set<number>();
  return (
    <>
      {refs.map((ref) => {
        const source = byRef(ref);
        if (!source) {
          return <FallbackMarker key={ref} citationRef={ref} />;
        }
        if (seen.has(source.number)) {
          return null;
        }
        seen.add(source.number);
        const citation = source.citations.find((c) => c.ref.toUpperCase() === ref.toUpperCase()) ?? source.citations[0]!;
        return (
          <Fragment key={ref}>
            <CitationMarker source={source} citation={citation} />
          </Fragment>
        );
      })}
    </>
  );
}

/** Outside a numbered answer (sub-agent notes), a known passage still gets a card, marked by its own tag. */
function FallbackMarker({ citationRef }: { citationRef: string }) {
  const citation = useCitation(citationRef);
  if (!citation) {
    return <UnknownMarker citationRef={citationRef} />;
  }
  const source: AnswerSource = {
    number: Number(citationRef.replace(/\D/g, '')) || 0,
    sourceId: citation.sourceId,
    title: citation.sourceTitle || 'Untitled source',
    url: safeExternalUrl(citation.sourceUrl),
    host: hostname(citation.sourceUrl),
    citations: [citation],
    cited: false,
  };
  return <CitationMarker source={source} citation={citation} />;
}
