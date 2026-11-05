import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { asArray, isRecord, request, type QueryValue } from '../../lib/api';
import type { Guid } from '../../lib/types';
import { SOURCE_POLL_INTERVAL_MS, sourcesRefetchInterval } from './polling';
import type {
  AddTextSourceRequest,
  AddUrlSourceRequest,
  ChunkDto,
  CreateSourceFolderRequest,
  MoveSourcesResponse,
  ReindexResponse,
  SearchHitDto,
  SearchRequest,
  SourceDto,
  SourceFolderDto,
  SourceFolderListDto,
  SourceListParams,
  SourcePageDto,
  SourceSiteDto,
  SourceStatsDto,
} from './types';

export const sourceKeys = {
  /** Prefix of everything derived from sources: invalidating it refreshes pages, stats, titles and chunks. */
  all: ['sources'] as const,
  pages: ['sources', 'page'] as const,
  page: (params: SourceListParams) => ['sources', 'page', normalizeSourceListParams(params)] as const,
  stats: ['sources', 'stats'] as const,
  folders: ['sources', 'folders'] as const,
  sites: (params: SourceListParams) => ['sources', 'sites', normalizeSourceListParams(params)] as const,
  title: (id: string) => ['sources', 'title', id.toLowerCase()] as const,
  chunks: (sourceId: string, skip: number, take: number) => ['sources', sourceId, 'chunks', skip, take] as const,
};

/**
 * Trimmed, de-duplicated, without empty values or the default offset, so equivalent parameters produce the same
 * query string and share one cache entry.
 */
export function normalizeSourceListParams(params: SourceListParams = {}): SourceListParams {
  const result: { -readonly [K in keyof SourceListParams]: SourceListParams[K] } = {};
  const query = params.query?.trim();
  if (query) {
    result.query = query;
  }
  if (params.type) {
    result.type = params.type;
  }
  const status = [...new Set(params.status ?? [])];
  if (status.length > 0) {
    result.status = status;
  }
  const ids = [...new Set((params.ids ?? []).map((id) => id.trim()).filter(Boolean))];
  if (ids.length > 0) {
    result.ids = ids;
  }
  if (params.offset != null && params.offset > 0) {
    result.offset = Math.floor(params.offset);
  }
  if (params.limit != null) {
    result.limit = params.limit;
  }
  const folder = params.folder?.trim();
  if (folder) {
    result.folder = folder;
  }
  const site = params.site?.trim().toLowerCase();
  if (site) {
    result.site = site;
  }
  const parent = params.parent?.trim();
  if (parent) {
    result.parent = parent;
  }
  return result;
}

export function sourceListQuery(params: SourceListParams = {}): Record<string, QueryValue> {
  const normalized = normalizeSourceListParams(params);
  return {
    query: normalized.query,
    type: normalized.type,
    status: normalized.status?.join(','),
    ids: normalized.ids?.join(','),
    offset: normalized.offset,
    limit: normalized.limit,
    folder: normalized.folder,
    site: normalized.site,
    parent: normalized.parent,
  };
}

// The filters of a list request, without its paging and ids, as POST /api/sources/move expects them.
function matchingFilters(params: SourceListParams) {
  const normalized = normalizeSourceListParams(params);
  return {
    query: normalized.query ?? null,
    type: normalized.type ?? null,
    status: normalized.status?.join(',') ?? null,
    folder: normalized.folder ?? null,
    site: normalized.site ?? null,
  };
}

function finiteNumber(value: unknown, fallback: number): number {
  return typeof value === 'number' && Number.isFinite(value) ? value : fallback;
}

/** Fills fields a page may lack; a bare array (API versions before paging) becomes a single page. */
function toSourcePage(value: unknown, params: SourceListParams): SourcePageDto {
  const page = isRecord(value) ? value : {};
  const items = asArray<SourceDto>(value);
  const offset = finiteNumber(page.offset, params.offset ?? 0);
  return {
    items,
    total: finiteNumber(page.total, offset + items.length),
    offset,
    limit: finiteNumber(page.limit, params.limit ?? items.length),
    nextOffset: typeof page.nextOffset === 'number' ? page.nextOffset : null,
  };
}

function toCounts(value: unknown): Record<string, number> {
  if (!isRecord(value)) {
    return {};
  }
  return Object.fromEntries(Object.entries(value).filter((entry): entry is [string, number] => typeof entry[1] === 'number'));
}

function toFolderList(value: unknown): SourceFolderListDto {
  const list = isRecord(value) ? value : {};
  return {
    folders: asArray<SourceFolderDto>(list.folders).map((folder) => ({ ...folder, isInternal: folder.isInternal === true })),
    unfiledCount: finiteNumber(list.unfiledCount, 0),
    totalCount: finiteNumber(list.totalCount, 0),
  };
}

function toSourceStats(value: unknown): SourceStatsDto {
  const stats = isRecord(value) ? value : {};
  return {
    total: finiteNumber(stats.total, 0),
    ready: finiteNumber(stats.ready, 0),
    pending: finiteNumber(stats.pending, 0),
    processing: finiteNumber(stats.processing, 0),
    failed: finiteNumber(stats.failed, 0),
    waiting: finiteNumber(stats.waiting, 0),
    readyChunks: finiteNumber(stats.readyChunks, 0),
    readyByType: toCounts(stats.readyByType),
    readyByEmbeddingModel: toCounts(stats.readyByEmbeddingModel),
  };
}

export const sourcesApi = {
  listSources: async (params: SourceListParams = {}, signal?: AbortSignal) =>
    toSourcePage(await request<unknown>('/api/sources', { query: sourceListQuery(params), signal }), normalizeSourceListParams(params)),
  getSourceStats: async (signal?: AbortSignal) => toSourceStats(await request<unknown>('/api/sources/stats', { signal })),
  addUrlSource: (body: AddUrlSourceRequest) => request<SourceDto | undefined>('/api/sources/url', { method: 'POST', body }),
  addTextSource: (body: AddTextSourceRequest) => request<SourceDto | undefined>('/api/sources/text', { method: 'POST', body }),
  uploadSource: (file: File, options: { title?: string; folderId?: Guid | null } = {}) => {
    const form = new FormData();
    form.append('file', file, file.name);
    if (options.title?.trim()) {
      form.append('title', options.title.trim());
    }
    if (options.folderId) {
      form.append('folderId', options.folderId);
    }
    return request<SourceDto | undefined>('/api/sources/upload', { method: 'POST', formData: form });
  },
  reingestSource: (id: Guid) => request<SourceDto | undefined>(`/api/sources/${encodeURIComponent(id)}/reingest`, { method: 'POST' }),
  reindexAll: () => request<ReindexResponse | undefined>('/api/sources/reindex', { method: 'POST' }),
  deleteSource: (id: Guid) => request<void>(`/api/sources/${encodeURIComponent(id)}`, { method: 'DELETE' }),
  listChunks: async (id: Guid, skip: number, take: number, signal?: AbortSignal) =>
    asArray<ChunkDto>(await request<unknown>(`/api/sources/${encodeURIComponent(id)}/chunks`, { query: { skip, take }, signal })),
  search: async (body: SearchRequest) => asArray<SearchHitDto>(await request<unknown>('/api/search', { method: 'POST', body })),
  listFolders: async (signal?: AbortSignal) => toFolderList(await request<unknown>('/api/sources/folders', { signal })),
  createFolder: (body: CreateSourceFolderRequest) => request<SourceFolderDto>('/api/sources/folders', { method: 'POST', body }),
  renameFolder: (id: Guid, name: string) =>
    request<SourceFolderDto>(`/api/sources/folders/${encodeURIComponent(id)}`, { method: 'PATCH', body: { name } }),
  moveFolder: (id: Guid, parentId: Guid | null) =>
    request<SourceFolderDto>(`/api/sources/folders/${encodeURIComponent(id)}/move`, { method: 'POST', body: { parentId } }),
  deleteFolder: (id: Guid) => request<void>(`/api/sources/folders/${encodeURIComponent(id)}`, { method: 'DELETE' }),
  moveSources: (sourceIds: readonly Guid[], folderId: Guid | null) =>
    request<MoveSourcesResponse>('/api/sources/move', { method: 'POST', body: { sourceIds, folderId } }),
  moveMatchingSources: (params: SourceListParams, folderId: Guid | null) =>
    request<MoveSourcesResponse>('/api/sources/move', { method: 'POST', body: { matching: matchingFilters(params), folderId } }),
  listSites: async (params: SourceListParams, signal?: AbortSignal) => {
    const { query, type, status, folder } = sourceListQuery(params);
    return asArray<SourceSiteDto>(await request<unknown>('/api/sources/sites', { query: { query, type, status, folder }, signal }));
  },
};

export interface SourcesPageOptions {
  enabled?: boolean;
  /** Milliseconds, or a function of the latest page; false (the default) does not poll. */
  refetchInterval?: number | false | ((page: SourcePageDto | undefined) => number | false);
}

export function useSourcesPage(params: SourceListParams, options: SourcesPageOptions = {}) {
  const normalized = normalizeSourceListParams(params);
  const refetchInterval = options.refetchInterval ?? false;
  return useQuery({
    queryKey: sourceKeys.page(normalized),
    queryFn: ({ signal }) => sourcesApi.listSources(normalized, signal),
    placeholderData: keepPreviousData,
    enabled: options.enabled ?? true,
    refetchInterval: typeof refetchInterval === 'function' ? (query) => refetchInterval(query.state.data) : refetchInterval,
  });
}

export function useSourceStats() {
  return useQuery({
    queryKey: sourceKeys.stats,
    queryFn: ({ signal }) => sourcesApi.getSourceStats(signal),
    refetchInterval: (query) => sourcesRefetchInterval(query.state.data),
  });
}

export function useSourceFolders() {
  return useQuery({
    queryKey: sourceKeys.folders,
    queryFn: ({ signal }) => sourcesApi.listFolders(signal),
  });
}

// Site counts ignore the site filter itself, so the other sites stay selectable.
export function useSourceSites(params: SourceListParams, enabled = true) {
  const normalized = normalizeSourceListParams({ query: params.query, type: params.type, status: params.status, folder: params.folder });
  return useQuery({
    queryKey: sourceKeys.sites(normalized),
    queryFn: ({ signal }) => sourcesApi.listSites(normalized, signal),
    placeholderData: keepPreviousData,
    enabled,
  });
}

export const WEBSITE_PAGES_LIMIT = 100;

/** A website's pages, polled while it is crawled so their statuses fill in. */
export function useWebsitePages(websiteId: Guid, crawling: boolean, offset = 0) {
  return useSourcesPage({ parent: websiteId, offset, limit: WEBSITE_PAGES_LIMIT }, { refetchInterval: crawling ? SOURCE_POLL_INTERVAL_MS : false });
}

export function useChunks(sourceId: string | null, skip: number, take: number) {
  return useQuery({
    queryKey: sourceKeys.chunks(sourceId ?? '', skip, take),
    queryFn: ({ signal }) => sourcesApi.listChunks(sourceId!, skip, take, signal),
    enabled: Boolean(sourceId),
    placeholderData: keepPreviousData,
  });
}
