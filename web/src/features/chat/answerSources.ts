import { createContext, useContext } from 'react';
import { hostname, safeExternalUrl } from '../../lib/format';
import type { CitationLookup } from './citationContext';
import type { CitationDto } from './types';

export interface AnswerSource {
  /** What the answer's markers show: 1, 2, 3 in order of first citation. */
  number: number;
  sourceId: string;
  title: string;
  url: string | null;
  host: string | null;
  citations: CitationDto[];
  /** Whether the answer text cites it, rather than only a tool or a sub-agent. */
  cited: boolean;
}

export interface AnswerSources {
  cited: AnswerSource[];
  byRef: (ref: string) => AnswerSource | undefined;
}

/** Passages of one source share a marker. Text citations come first, then ones only tools or a sub-agent cited. */
export function numberSources(textRefs: readonly string[], lookup: CitationLookup, extra: readonly CitationDto[] = []): AnswerSources {
  const bySource = new Map<string, AnswerSource>();
  const byRef = new Map<string, AnswerSource>();
  const add = (citation: CitationDto, cited: boolean) => {
    const key = citation.sourceId.toLowerCase();
    let source = bySource.get(key);
    if (!source) {
      source = {
        number: bySource.size + 1,
        sourceId: citation.sourceId,
        title: citation.sourceTitle || 'Untitled source',
        url: safeExternalUrl(citation.sourceUrl),
        host: hostname(citation.sourceUrl),
        citations: [],
        cited,
      };
      bySource.set(key, source);
    }
    const ref = citation.ref.toUpperCase();
    if (!byRef.has(ref)) {
      source.citations.push(citation);
      byRef.set(ref, source);
    }
  };
  for (const ref of textRefs) {
    const citation = lookup(ref);
    if (citation) {
      add(citation, true);
    }
  }
  for (const citation of extra) {
    add(citation, false);
  }
  return {
    cited: [...bySource.values()].filter((source) => source.cited),
    byRef: (ref) => byRef.get(ref.toUpperCase()),
  };
}

const EMPTY: AnswerSources = { cited: [], byRef: () => undefined };

export const AnswerSourcesContext = createContext<AnswerSources>(EMPTY);

export function useAnswerSources(): AnswerSources {
  return useContext(AnswerSourcesContext);
}

/** "Page 4", "Pages 4, 9", or nothing for sources without pages. */
export function pagesLabel(citations: readonly CitationDto[]): string | null {
  const pages = [...new Set(citations.map((citation) => citation.page).filter((page): page is number => page != null))].sort((a, b) => a - b);
  if (pages.length === 0) {
    return null;
  }
  return `${pages.length === 1 ? 'Page' : 'Pages'} ${pages.join(', ')}`;
}
