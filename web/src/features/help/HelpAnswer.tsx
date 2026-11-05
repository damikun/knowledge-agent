import { ArrowUpRight, BookOpen } from 'lucide-react';
import { useCallback, useMemo, type ReactNode } from 'react';
import { Link } from 'react-router';
import { citationMarkerClass } from '../../components/ui/CitationMarker';
import { extractCitationRefs } from '../chat/citations';
import { Markdown, type RenderLink } from '../chat/Markdown';
import { TypingDots } from '../chat/ThinkingBlock';
import type { CitationDto } from '../chat/types';
import { portalPath } from './api';
import { useReturnState } from '../../lib/returnTo';

interface SectionLink {
  key: string;
  path: string;
  link: string | null;
}

function SectionCard({ section, onNavigate }: { section: SectionLink; onNavigate?: () => void }) {
  const returnState = useReturnState();
  return (
    <li className="min-w-0 rounded-lg border border-border bg-surface px-3 py-2.5">
      <div className="flex min-w-0 items-start gap-2">
        <BookOpen className="mt-0.5 size-3.5 shrink-0 text-fg-muted" aria-hidden />
        <div className="min-w-0 flex-1">
          <p className="text-sm font-medium break-words text-fg">{section.path}</p>
          {section.link ? (
            <Link
              to={section.link} state={returnState}
              onClick={onNavigate}
              className="mt-1 inline-flex items-center gap-1 text-xs font-medium text-accent-text hover:underline"
              aria-label={`Open page: ${section.path}`}
            >
              Open page
              <ArrowUpRight className="size-3.5" aria-hidden />
            </Link>
          ) : null}
        </div>
      </div>
    </li>
  );
}

function SectionList({ label, sections, onNavigate }: { label: string; sections: readonly SectionLink[]; onNavigate?: () => void }) {
  return (
    <ul aria-label={label} className="space-y-1.5">
      {sections.map((section) => (
        <SectionCard key={section.key} section={section} onNavigate={onNavigate} />
      ))}
    </ul>
  );
}

export interface HelpAnswerProps {
  text: string;
  citations: readonly CitationDto[];
  streaming?: boolean;
  activity?: ReactNode;
  onNavigate?: () => void;
}

/** Renders an answer; [S1] tags become links to the cited help section, and portal paths stay in the portal. */
export function HelpAnswer({ text, citations, streaming = false, activity, onNavigate }: HelpAnswerProps) {
  const returnState = useReturnState();
  const byRef = useMemo(() => new Map(citations.map((citation) => [citation.ref.toUpperCase(), citation] as const)), [citations]);
  const cited = useMemo(() => {
    const seen = new Set<string>();
    return extractCitationRefs(text).flatMap((ref) => {
      const citation = byRef.get(ref);
      if (!citation || seen.has(citation.sourceId)) {
        return [];
      }
      seen.add(citation.sourceId);
      return [citation];
    });
  }, [text, byRef]);
  const numbers = useMemo(() => new Map(cited.map((citation, index) => [citation.sourceId, index + 1] as const)), [cited]);

  // Memoized, or the Markdown re-renders on every panel change.
  const renderCitation = useCallback(
    (ref: string) => {
      const citation = byRef.get(ref.toUpperCase());
      const number = citation ? numbers.get(citation.sourceId) : undefined;
      if (!citation || number === undefined) {
        return null;
      }
      const to = portalPath(citation.sourceUrl);
      return to ? (
        <Link to={to} state={returnState} onClick={onNavigate} className={citationMarkerClass()} aria-label={`Help section ${number}: ${citation.sourceTitle}`}>
          {number}
        </Link>
      ) : (
        <span className={citationMarkerClass()} title={citation.sourceTitle}>
          {number}
        </span>
      );
    },
    [byRef, numbers, onNavigate, returnState],
  );

  const renderLink = useCallback<RenderLink>(
    (href, children) => {
      const to = portalPath(href);
      return to ? (
        <Link to={to} state={returnState} onClick={onNavigate}>
          {children}
        </Link>
      ) : null;
    },
    [onNavigate, returnState],
  );

  return (
    <article aria-label="Help answer" aria-busy={streaming} className="space-y-3">
      {text ? (
        <Markdown text={text} renderCitation={renderCitation} renderLink={renderLink} />
      ) : streaming ? (
        <div className="flex items-center gap-2 py-1 text-xs text-fg-muted" role="status">
          <TypingDots />
          <span>{activity ?? 'Writing the answer…'}</span>
        </div>
      ) : null}
      {!streaming && cited.length > 0 ? (
        <section className="space-y-1.5">
          <h3 className="text-xs font-medium text-fg-muted">From the help</h3>
          <SectionList
            label="Help sections cited"
            onNavigate={onNavigate}
            sections={cited.map((citation) => ({
              key: citation.sourceId,
              path: `${numbers.get(citation.sourceId)}. ${citation.sourceTitle}`,
              link: portalPath(citation.sourceUrl),
            }))}
          />
        </section>
      ) : null}
    </article>
  );
}
