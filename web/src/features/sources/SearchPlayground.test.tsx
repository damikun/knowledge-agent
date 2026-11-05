import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { axeViolations } from '../../test/a11y';
import { jsonResponse, mockFetch, renderWithProviders } from '../../test/utils';
import { hitSignals, SearchPlayground } from './SearchPlayground';
import type { SearchHitDto, SearchRequest } from './types';

afterEach(() => {
  vi.unstubAllGlobals();
});

function hit(overrides: Partial<SearchHitDto> = {}): SearchHitDto {
  return {
    chunkId: 'c1',
    sourceId: 's1',
    chunkIndex: 0,
    sourceTitle: 'Robot error codes',
    sourceUrl: null,
    page: null,
    content: 'Error E-214 means an encoder mismatch on joint 2.',
    score: 0.91,
    ...overrides,
  };
}

describe('SearchPlayground', () => {
  it('sends the chosen mode and rerank setting and shows what each retriever found', async () => {
    const requests: SearchRequest[] = [];
    mockFetch({
      'POST /api/search': (_url: URL, init: RequestInit) => {
        requests.push(JSON.parse(String(init.body)) as SearchRequest);
        return jsonResponse([
          hit({ vectorRank: 2, vectorScore: 0.8123, keywordRank: 1, keywordScore: 7.314, rerankScore: 0.91 }),
          hit({ chunkId: 'c2', chunkIndex: 1, score: 0.84, vectorRank: 1, vectorScore: 0.84 }),
        ]);
      },
    });
    const user = userEvent.setup();
    renderWithProviders(<SearchPlayground />);

    await user.type(screen.getByLabelText('Search query'), 'what does E-214 mean');
    await user.click(screen.getByRole('button', { name: 'Search' }));
    await screen.findByRole('list', { name: 'Search results' });
    expect(requests[0]).toMatchObject({ query: 'what does E-214 mean', topK: 5, mode: null, rerank: null });

    await user.click(screen.getByRole('combobox', { name: 'Mode' }));
    await user.click(screen.getByRole('option', { name: 'Keyword' }));
    await user.click(screen.getByRole('combobox', { name: 'Rerank' }));
    await user.click(screen.getByRole('option', { name: 'Off' }));
    await user.click(screen.getByRole('button', { name: 'Search' }));
    await vi.waitFor(() => expect(requests).toHaveLength(2));
    expect(requests[1]).toMatchObject({ mode: 'keyword', rerank: false });

    const [first, second] = screen.getAllByRole('list', { name: 'Retrieval details' });
    expect(within(first!).getAllByRole('listitem').map((item) => item.textContent)).toEqual([
      'Vector #2 · 0.812 (Rank and cosine similarity in the vector results)',
      'Keyword #1 · 7.31 (Rank and BM25 score in the keyword results)',
      'Reranked · 0.910 (Relevance from the reranker; it orders the results)',
    ]);
    expect(within(second!).getAllByRole('listitem')).toHaveLength(1);
    expect(await axeViolations()).toEqual([]);
  });

  it('lists no retrieval details for hits without component scores', () => {
    expect(hitSignals(hit())).toEqual([]);
    expect(hitSignals(hit({ keywordRank: 3, keywordScore: null })).map((signal) => signal.label)).toEqual(['Keyword #3']);
  });
});
