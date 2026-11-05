import { useMutation } from '@tanstack/react-query';
import { ApiError, isRecord, request } from '../../lib/api';
import type { Guid } from '../../lib/types';
import type { CitationDto } from '../chat/types';

export interface ReplySuggestionDto {
  text: string;
  citations: CitationDto[];
}

function toCitation(value: unknown): CitationDto | null {
  if (!isRecord(value) || typeof value.ref !== 'string' || typeof value.sourceId !== 'string') {
    return null;
  }
  return {
    ref: value.ref,
    sourceId: value.sourceId,
    chunkId: typeof value.chunkId === 'string' ? value.chunkId : '',
    sourceTitle: typeof value.sourceTitle === 'string' && value.sourceTitle ? value.sourceTitle : 'Untitled source',
    sourceUrl: typeof value.sourceUrl === 'string' ? value.sourceUrl : null,
    page: typeof value.page === 'number' ? value.page : null,
    snippet: typeof value.snippet === 'string' ? value.snippet : '',
    score: typeof value.score === 'number' ? value.score : 0,
  };
}

export function toReplySuggestion(value: unknown): ReplySuggestionDto {
  const dto = isRecord(value) ? value : {};
  const citations = Array.isArray(dto.citations) ? dto.citations.map(toCitation).filter((item): item is CitationDto => item !== null) : [];
  return { text: typeof dto.text === 'string' ? dto.text : '', citations };
}

async function suggestReply(conversationId: Guid): Promise<ReplySuggestionDto> {
  return toReplySuggestion(await request<unknown>(`/api/conversations/${encodeURIComponent(conversationId)}/suggest-reply`, { method: 'POST' }));
}

/** The API returns 503 with this code when no chat model is connected. */
export function isModelNotConfigured(error: unknown): boolean {
  return error instanceof ApiError && error.problem?.code === 'model_not_configured';
}

/** Each source once, in the order the draft cites them. */
export function citedSources(citations: readonly CitationDto[]): Array<{ sourceId: Guid; title: string; refs: string[] }> {
  const bySource = new Map<Guid, { sourceId: Guid; title: string; refs: string[] }>();
  for (const citation of citations) {
    const entry = bySource.get(citation.sourceId) ?? { sourceId: citation.sourceId, title: citation.sourceTitle, refs: [] };
    entry.refs.push(citation.ref);
    bySource.set(citation.sourceId, entry);
  }
  return [...bySource.values()];
}

export function useSuggestReply() {
  return useMutation({ mutationFn: (conversationId: Guid) => suggestReply(conversationId) });
}
