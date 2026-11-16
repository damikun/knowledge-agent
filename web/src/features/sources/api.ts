import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { asArray, call, isRecord, type QueryValue } from '../../lib/api';
import { endpoints } from '../../lib/endpoints';
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
  all: ['sources'] as const,
  pages: ['sources', 'page'] as const,
  page: (params: SourceListParams) => ['sources', 'page', normalizeSourceListParams(params)] as const,
  stats: ['sources', 'stats'] as const,
  folders: ['sources', 'folders'] as const,
  sites: (params: SourceListParams) => ['sources', 'sites', normalizeSourceListParams(params)] as const,
  title: (id: string) => ['sources', 'title', id.toLowerCase()] as const,
  chunks: (sourceId: string, skip: number, take: number) => ['sources', sourceId, 'chunks', skip, take] as const,
};

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
    toSourcePage(await call(endpoints.sources.list, { query: sourceListQuery(params), signal }), normalizeSourceListParams(params)),
  getSourceStats: async (signal?: AbortSignal) => toSourceStats(await call(endpoints.sources.stats, { signal })),
  addUrlSource: (body: AddUrlSourceRequest): Promise<SourceDto | undefined> => call(endpoints.sources.addUrl, { body }),
  addTextSource: (body: AddTextSourceRequest): Promise<SourceDto | undefined> => call(endpoints.sources.addText, { body }),
  uploadSource: (file: File, options: { title?: string; folderId?: Guid | null } = {}): Promise<SourceDto | undefined> => {
    const form = new FormData();
    form.append('file', file, file.name);
    if (options.title?.trim()) {
      form.append('title', options.title.trim());
    }
    if (options.folderId) {
      form.append('folderId', options.folderId);
    }
    return call(endpoints.sources.upload, { formData: form });
  },
  reingestSource: (id: Guid): Promise<SourceDto | undefined> => call(endpoints.sources.reingest, { params: { id } }),
  reindexAll: (): Promise<ReindexResponse | undefined> => call(endpoints.sources.reindexAll),
  deleteSource: (id: Guid): Promise<void> => call(endpoints.sources.remove, { params: { id } }),
  listChunks: async (id: Guid, skip: number, take: number, signal?: AbortSignal) =>
    asArray<ChunkDto>(await call(endpoints.sources.chunks, { params: { id }, query: { skip, take }, signal })),
  search: async (body: SearchRequest) => asArray<SearchHitDto>(await call(endpoints.sources.search, { body })),
  listFolders: async (signal?: AbortSignal) => toFolderList(await call(endpoints.sources.listFolders, { signal })),
  createFolder: (body: CreateSourceFolderRequest): Promise<SourceFolderDto> => call(endpoints.sources.createFolder, { body }),
  renameFolder: (id: Guid, name: string): Promise<SourceFolderDto> =>
    call(endpoints.sources.renameFolder, { params: { id }, body: { name } }),
  moveFolder: (id: Guid, parentId: Guid | null): Promise<SourceFolderDto> =>
    call(endpoints.sources.moveFolder, { params: { id }, body: { parentId } }),
  deleteFolder: (id: Guid): Promise<void> => call(endpoints.sources.deleteFolder, { params: { id } }),
  moveSources: (sourceIds: readonly Guid[], folderId: Guid | null): Promise<MoveSourcesResponse> =>
    call(endpoints.sources.move, { body: { sourceIds, folderId } }),
  moveMatchingSources: (params: SourceListParams, folderId: Guid | null): Promise<MoveSourcesResponse> =>
    call(endpoints.sources.move, { body: { matching: matchingFilters(params), folderId } }),
  listSites: async (params: SourceListParams, signal?: AbortSignal) => {
    const { query, type, status, folder } = sourceListQuery(params);
    return asArray<SourceSiteDto>(await call(endpoints.sources.sites, { query: { query, type, status, folder }, signal }));
  },
};

export interface SourcesPageOptions {
  enabled?: boolean;
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
