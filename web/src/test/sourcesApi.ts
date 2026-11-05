import type { SourceDto, SourceFolderDto, SourceFolderListDto, SourcePageDto, SourceSiteDto, SourceStatsDto } from '../features/sources/types';
import { sourceDto } from './fixtures';
import { jsonResponse, type FetchHandler } from './utils';

/** A GUID-shaped source id (`n` = 1 → "00000000-0000-4000-8000-000000000001"). */
export function sourceGuid(n: number): string {
  return `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`;
}

/** `count` ready sources "Source 1" … "Source n"; higher numbers are newer. */
export function numberedSources(count: number, overrides: (n: number) => Partial<SourceDto> = () => ({})): SourceDto[] {
  return Array.from({ length: count }, (_, index) => {
    const n = index + 1;
    return sourceDto({
      id: sourceGuid(n),
      title: `Source ${n}`,
      fileName: `source-${n}.pdf`,
      createdAt: new Date(Date.UTC(2026, 9, 1, 0, n)).toISOString(),
      ...overrides(n),
    });
  });
}

export interface SourceFilterValues {
  query?: string | null;
  type?: string | null;
  status?: string | null;
  folder?: string | null;
  site?: string | null;
}

/** The sources the filters of GET /api/sources (or the matching part of POST /api/sources/move) select. */
export function filterSources(sources: readonly SourceDto[], filters: SourceFilterValues): SourceDto[] {
  const words = (filters.query ?? '').trim().toLowerCase().split(/\s+/).filter(Boolean);
  const statuses = (filters.status ?? '').split(',').filter(Boolean);
  const { type, folder, site } = filters;
  const text = (s: SourceDto) => [s.title, s.url ?? '', s.fileName ?? ''].join(' ').toLowerCase();
  return sources
    .filter((s) => words.every((word) => text(s).includes(word)))
    .filter((s) => !type || s.type === type)
    .filter((s) => statuses.length === 0 || statuses.includes(s.status))
    .filter((s) => !folder || (folder === 'unfiled' ? s.folderId === null : s.folderId === folder))
    .filter((s) => !site || s.site === site);
}

const filterValues = (params: URLSearchParams): SourceFilterValues => ({
  query: params.get('query'),
  type: params.get('type'),
  status: params.get('status'),
  folder: params.get('folder'),
  site: params.get('site'),
});

/** GET /api/sources as the API answers it: filtering, ordering and paging over `sources`. */
function sourcePage(sources: readonly SourceDto[], url: URL): SourcePageDto {
  const params = url.searchParams;
  const query = (params.get('query') ?? '').trim().toLowerCase();
  const ids = (params.get('ids') ?? '')
    .split(',')
    .filter(Boolean)
    .map((id) => id.toLowerCase());
  const offset = Number(params.get('offset') ?? 0);
  const limit = Number(params.get('limit') ?? 25);
  // Without a parent the API leaves crawled pages out, unless ids name them.
  const parent = params.get('parent');
  const startsWithQuery = (s: SourceDto) => Number(s.title.toLowerCase().startsWith(query));
  const matches = filterSources(sources, filterValues(params))
    .filter((s) => ids.length === 0 || ids.includes(s.id.toLowerCase()))
    .filter((s) => (parent ? s.parentId === parent : ids.length > 0 || !s.parentId))
    .sort(
      query ? (a, b) => startsWithQuery(b) - startsWithQuery(a) || a.title.localeCompare(b.title) : (a, b) => b.createdAt.localeCompare(a.createdAt),
    );
  const items = matches.slice(offset, offset + limit);
  return { items, total: matches.length, offset, limit, nextOffset: offset + limit < matches.length ? offset + limit : null };
}

function countBy(sources: readonly SourceDto[], key: (source: SourceDto) => string | null): Record<string, number> {
  const counts: Record<string, number> = {};
  for (const source of sources) {
    const value = key(source);
    if (value) {
      counts[value] = (counts[value] ?? 0) + 1;
    }
  }
  return counts;
}

/** GET /api/sources/stats for `sources`. */
export function sourceStats(sources: readonly SourceDto[]): SourceStatsDto {
  const ready = sources.filter((s) => s.status === 'ready');
  // Statuses count what the library lists, so the pages of a website are left out.
  const listed = sources.filter((s) => !s.parentId);
  const withStatus = (status: SourceDto['status']) => listed.filter((s) => s.status === status).length;
  return {
    total: listed.length,
    ready: withStatus('ready'),
    pending: withStatus('pending'),
    processing: withStatus('processing'),
    failed: withStatus('failed'),
    waiting: withStatus('waiting'),
    readyChunks: ready.reduce((sum, s) => sum + s.chunkCount, 0),
    readyByType: countBy(ready, (s) => s.type),
    readyByEmbeddingModel: countBy(ready, (s) => s.embeddingModel),
  };
}

export function sourceFolder(id: string, name: string, parentId: string | null = null): SourceFolderDto {
  return { id, name, parentId, sourceCount: 0, createdAt: '2026-10-20T08:00:00Z', updatedAt: '2026-10-20T08:00:00Z' };
}

/** GET /api/sources/sites: sites of the matching sources, the most first. */
function siteCounts(sources: readonly SourceDto[], url: URL): SourceSiteDto[] {
  const counts = countBy(filterSources(sources, { ...filterValues(url.searchParams), site: null }), (s) => s.site);
  return Object.entries(counts)
    .map(([site, count]) => ({ site, count }))
    .sort((a, b) => b.count - a.count || a.site.localeCompare(b.site));
}

/** GET /api/sources/folders for `folders`, counting `sources` per folder. */
function folderList(folders: readonly SourceFolderDto[], sources: readonly SourceDto[]): SourceFolderListDto {
  return {
    folders: folders.map((folder) => ({ ...folder, sourceCount: sources.filter((s) => s.folderId === folder.id).length })),
    unfiledCount: sources.filter((s) => s.folderId === null).length,
    totalCount: sources.length,
  };
}

/** mockFetch routes for GET /api/sources, /stats and /folders; the getters are read per request, so tests can change the data. */
export function sourcesRoutes(
  getSources: () => readonly SourceDto[],
  getFolders: () => readonly SourceFolderDto[] = () => [],
): Record<string, FetchHandler> {
  return {
    'GET /api/sources': (url) => jsonResponse(sourcePage(getSources(), url)),
    'GET /api/sources/stats': () => jsonResponse(sourceStats(getSources())),
    'GET /api/sources/folders': () => jsonResponse(folderList(getFolders(), getSources())),
    'GET /api/sources/sites': (url) => jsonResponse(siteCounts(getSources(), url)),
  };
}
