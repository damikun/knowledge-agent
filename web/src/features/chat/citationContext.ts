import { createContext, useContext } from 'react';
import type { CitationDto } from './types';

export type CitationLookup = (ref: string) => CitationDto | undefined;

const emptyLookup: CitationLookup = () => undefined;

export const CitationContext = createContext<CitationLookup>(emptyLookup);

export function useCitation(ref: string): CitationDto | undefined {
  return useContext(CitationContext)(ref);
}

export type SourceTitleLookup = (sourceId: string) => string | undefined;

/** Titles already known on the page, e.g. from citations; tool cards fetch the others by id. */
export const SourceTitleContext = createContext<SourceTitleLookup>(() => undefined);

export function useSourceTitleLookup(): SourceTitleLookup {
  return useContext(SourceTitleContext);
}

/** Earlier sources take precedence. */
export function createCitationLookup(...sources: Array<ReadonlyMap<string, CitationDto> | readonly CitationDto[]>): CitationLookup {
  const maps = sources.map((source) =>
    source instanceof Map ? source : new Map((source as readonly CitationDto[]).map((c) => [c.ref.toUpperCase(), c] as const)),
  );
  return (ref: string) => {
    const key = ref.toUpperCase();
    for (const map of maps) {
      const hit = map.get(key) ?? map.get(ref);
      if (hit) {
        return hit;
      }
    }
    return undefined;
  };
}
