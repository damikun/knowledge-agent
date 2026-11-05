import { QueryClientProvider } from '@tanstack/react-query';
import { renderHook, waitFor } from '@testing-library/react';
import type { ReactNode } from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { numberedSources, sourceGuid, sourcesRoutes } from '../../test/sourcesApi';
import { createTestQueryClient, mockFetch, requestUrls } from '../../test/utils';
import { isSourceId, useSourceTitles } from './useSourceTitles';

afterEach(() => {
  vi.unstubAllGlobals();
});

function wrapperFor(queryClient = createTestQueryClient()) {
  return ({ children }: { children: ReactNode }) => <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
}

describe('useSourceTitles', () => {
  it('fetches unknown ids in batches of at most 100 and caches them per id', async () => {
    const fetchMock = mockFetch(sourcesRoutes(() => numberedSources(150)));
    const ids = Array.from({ length: 150 }, (_, i) => sourceGuid(i + 1));
    const wrapper = wrapperFor();
    const { result } = renderHook(() => useSourceTitles(ids), { wrapper });

    await waitFor(() => expect(result.current(sourceGuid(150))).toBe('Source 150'));
    expect(result.current(sourceGuid(1))).toBe('Source 1');
    const requests = requestUrls(fetchMock, 'GET /api/sources');
    expect(requests.map((url) => url.searchParams.get('ids')!.split(',').length)).toEqual([100, 50]);
    expect(requests.every((url) => url.searchParams.get('limit') === '100')).toBe(true);

    const again = renderHook(() => useSourceTitles([sourceGuid(5), sourceGuid(6)]), { wrapper });
    expect(again.result.current(sourceGuid(5))).toBe('Source 5');
    expect(requestUrls(fetchMock, 'GET /api/sources')).toHaveLength(2);
  });

  it('returns undefined for missing sources and while the request fails', async () => {
    const fetchMock = mockFetch({ 'GET /api/sources': () => new Response('Bad Request', { status: 400 }) });
    const { result } = renderHook(() => useSourceTitles([sourceGuid(1)]), { wrapper: wrapperFor() });
    await waitFor(() => expect(requestUrls(fetchMock, 'GET /api/sources')).toHaveLength(1));
    expect(result.current(sourceGuid(1))).toBeUndefined();
  });

  it('only looks up GUIDs', () => {
    expect(isSourceId(sourceGuid(1))).toBe(true);
    expect(isSourceId(sourceGuid(1).toUpperCase())).toBe(true);
    expect(isSourceId('src-handbook')).toBe(false);
    expect(isSourceId('')).toBe(false);
  });
});
