import { afterEach, describe, expect, it, vi } from 'vitest';
import { jsonResponse, mockFetch } from '../../test/utils';
import { normalizeSourceListParams, sourceListQuery, sourcesApi } from './api';

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('sourcesApi', () => {
  it('uploads files as multipart with the "file" field', async () => {
    const fetchMock = mockFetch({ 'POST /api/sources/upload': () => jsonResponse({ id: 'new' }, 202) });
    const file = new File(['%PDF-1.7'], 'report.pdf', { type: 'application/pdf' });
    await sourcesApi.uploadSource(file, { title: 'Quarterly report', folderId: 'f-legal' });
    await sourcesApi.uploadSource(file);
    const calls = fetchMock.mock.calls as unknown as Array<[string, RequestInit]>;
    const init = calls[0]![1];
    const form = init.body as FormData;
    expect(form).toBeInstanceOf(FormData);
    expect((form.get('file') as File).name).toBe('report.pdf');
    expect(form.get('title')).toBe('Quarterly report');
    expect(form.get('folderId')).toBe('f-legal');
    expect(init.headers).not.toHaveProperty('Content-Type');
    expect(init.headers).toMatchObject({ 'X-KA-Request': '1' });
    expect([...(calls[1]![1].body as FormData).keys()]).toEqual(['file']);
  });

  it('lists sources with only the non-empty parameters in the query string', async () => {
    const page = { items: [], total: 0, offset: 0, limit: 25, nextOffset: null };
    const fetchMock = mockFetch({ 'GET /api/sources': page });

    await expect(sourcesApi.listSources()).resolves.toEqual(page);
    await sourcesApi.listSources({ query: '   ', type: null, status: [], ids: [], offset: 0, limit: null });
    await sourcesApi.listSources({ query: ' employee handbook ', type: 'pdf', status: ['pending', 'processing'], offset: 25, limit: 25 });
    await sourcesApi.listSources({ status: ['ready'], limit: 50 });
    await sourcesApi.listSources({ ids: ['a', 'b', 'a', ' '], limit: 100 });
    await sourcesApi.listSources({ folder: 'unfiled', query: 'notes' });
    await sourcesApi.listSources({ folder: ' f-legal ', status: ['ready'] });
    await sourcesApi.listSources({ folder: '  ' });

    expect(fetchMock.mock.calls.map(([url]) => String(url))).toEqual([
      '/api/sources',
      '/api/sources',
      '/api/sources?query=employee+handbook&type=pdf&status=pending,processing&offset=25&limit=25',
      '/api/sources?status=ready&limit=50',
      '/api/sources?ids=a,b&limit=100',
      '/api/sources?query=notes&folder=unfiled',
      '/api/sources?status=ready&folder=f-legal',
      '/api/sources',
    ]);
  });

  it('treats equivalent source list parameters alike', () => {
    expect(normalizeSourceListParams({ query: ' x ', type: null, status: [], ids: [], offset: 0 })).toEqual({ query: 'x' });
    expect(sourceListQuery({ status: ['pending', 'processing', 'pending'], ids: ['s1', 's2'] })).toEqual({
      query: undefined,
      type: undefined,
      status: 'pending,processing',
      ids: 's1,s2',
      offset: undefined,
      limit: undefined,
    });
  });

  it('fills fields a source page or the stats lack', async () => {
    mockFetch({
      'GET /api/sources': { items: [{ id: 's1', title: 'One' }], total: 30 },
      'GET /api/sources/stats': { total: 3, ready: 2, failed: 1, readyByType: { pdf: 2 } },
    });
    await expect(sourcesApi.listSources({ offset: 25, limit: 25 })).resolves.toEqual({
      items: [{ id: 's1', title: 'One' }],
      total: 30,
      offset: 25,
      limit: 25,
      nextOffset: null,
    });
    await expect(sourcesApi.getSourceStats()).resolves.toEqual({
      total: 3,
      ready: 2,
      pending: 0,
      processing: 0,
      failed: 1,
      waiting: 0,
      readyChunks: 0,
      readyByType: { pdf: 2 },
      readyByEmbeddingModel: {},
    });
  });

  it('sends the folder requests the Sources page uses', async () => {
    const folder = { id: 'f/1', name: 'Legal', parentId: null, sourceCount: 2, createdAt: '2026-10-28T09:00:00Z', updatedAt: '2026-10-28T09:00:00Z' };
    const fetchMock = mockFetch({
      'GET /api/sources/folders': { folders: [folder] },
      'POST /api/sources/folders': () => jsonResponse(folder, 201),
      'PATCH /api/sources/folders/f%2F1': folder,
      'POST /api/sources/folders/f%2F1/move': folder,
      'DELETE /api/sources/folders/f%2F1': () => new Response(null, { status: 204 }),
      'POST /api/sources/move': { moved: 2 },
    });

    await expect(sourcesApi.listFolders()).resolves.toEqual({ folders: [{ ...folder, isInternal: false }], unfiledCount: 0, totalCount: 0 });
    await sourcesApi.createFolder({ name: 'Legal', parentId: null });
    await sourcesApi.renameFolder('f/1', 'Legal and compliance');
    await sourcesApi.moveFolder('f/1', 'f-parent');
    await sourcesApi.deleteFolder('f/1');
    await expect(sourcesApi.moveSources(['s1', 's2'], null)).resolves.toEqual({ moved: 2 });

    const calls = fetchMock.mock.calls.map(([url, init]) => [
      `${(init as RequestInit).method ?? 'GET'} ${String(url)}`,
      (init as RequestInit).body ? JSON.parse((init as RequestInit).body as string) : null,
    ]);
    expect(calls).toEqual([
      ['GET /api/sources/folders', null],
      ['POST /api/sources/folders', { name: 'Legal', parentId: null }],
      ['PATCH /api/sources/folders/f%2F1', { name: 'Legal and compliance' }],
      ['POST /api/sources/folders/f%2F1/move', { parentId: 'f-parent' }],
      ['DELETE /api/sources/folders/f%2F1', null],
      ['POST /api/sources/move', { sourceIds: ['s1', 's2'], folderId: null }],
    ]);
  });

  it('moves every matching source with the list filters and counts sites without the site filter', async () => {
    const fetchMock = mockFetch({
      'POST /api/sources/move': { moved: 340 },
      'GET /api/sources/sites': { items: [{ site: 'docs.python.org', count: 2 }] },
    });

    await sourcesApi.moveMatchingSources({ query: ' tutorial ', type: 'url', status: ['ready', 'failed'], folder: 'unfiled', site: 'Docs.Python.org', offset: 50, limit: 25, ids: ['x'] }, 'f-docs');
    await sourcesApi.moveMatchingSources({}, null);
    await expect(sourcesApi.listSites({ query: 'tutorial', site: 'docs.python.org', folder: 'f-docs', offset: 25 })).resolves.toEqual([
      { site: 'docs.python.org', count: 2 },
    ]);

    const [first, second, sites] = fetchMock.mock.calls as unknown as Array<[string, RequestInit]>;
    expect(JSON.parse(first![1].body as string)).toEqual({
      matching: { query: 'tutorial', type: 'url', status: 'ready,failed', folder: 'unfiled', site: 'docs.python.org' },
      folderId: 'f-docs',
    });
    expect(JSON.parse(second![1].body as string)).toEqual({ matching: { query: null, type: null, status: null, folder: null, site: null }, folderId: null });
    expect(String(sites![0])).toBe('/api/sources/sites?query=tutorial&folder=f-docs');
  });

  it('builds paging query strings and encodes path segments', async () => {
    const fetchMock = mockFetch({ 'GET /api/sources/a%2Fb/chunks': [] });
    await sourcesApi.listChunks('a/b', 20, 20);
    expect(String(fetchMock.mock.calls[0]![0])).toBe('/api/sources/a%2Fb/chunks?skip=20&take=20');
  });
});
