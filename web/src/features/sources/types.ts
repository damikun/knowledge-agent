import type { Guid, IsoDateTime } from '../../lib/types';

export type SourceType = 'url' | 'pdf' | 'text' | 'markdown' | 'website';
export type SourceStatus = 'pending' | 'processing' | 'ready' | 'failed' | 'waiting' | 'needsbrowser';

export type CrawlScope = 'page' | 'section' | 'site';

export interface WebsiteCrawlDto {
  scope: CrawlScope;
  maxPages: number;
  exclude: string[];
  pagesFound: number;
  pagesDone: number;
  pagesFailed: number;
}
export type IngestionStage = 'fetching' | 'extracting' | 'chunking' | 'contextualizing' | 'embedding' | 'storing';

export interface SourceDto {
  id: Guid;
  type: SourceType;
  status: SourceStatus;
  title: string;
  url: string | null;
  site: string | null;
  fileName: string | null;
  contentType: string | null;
  sizeBytes: number | null;
  chunkCount: number;
  pageCount: number | null;
  tokenEstimate: number;
  embeddingModel: string | null;
  error: string | null;
  attempts: number;
  stage: IngestionStage | null;
  embeddedChunks: number;
  jobRunId: string | null;
  jobRunUrl: string | null;
  createdAt: IsoDateTime;
  updatedAt: IsoDateTime;
  processedAt: IsoDateTime | null;
  metadata: Record<string, string>;
  folderId: Guid | null;
  parentId?: Guid | null;
  crawl?: WebsiteCrawlDto | null;
  lastFetchedAt?: IsoDateTime | null;
}

export interface SourcePageDto {
  items: SourceDto[];
  total: number;
  offset: number;
  limit: number;
  nextOffset: number | null;
}

export interface SourceListParams {
  query?: string | null;
  type?: SourceType | null;
  status?: readonly SourceStatus[] | null;
  ids?: readonly Guid[] | null;
  offset?: number | null;
  limit?: number | null;
  folder?: Guid | 'unfiled' | null;
  site?: string | null;
  parent?: Guid | null;
}

export interface SourceSiteDto {
  site: string;
  count: number;
}

export interface SourceFolderDto {
  id: Guid;
  name: string;
  parentId: Guid | null;
  sourceCount: number;
  createdAt: IsoDateTime;
  updatedAt: IsoDateTime;
  isInternal?: boolean;
}

export interface SourceFolderListDto {
  folders: SourceFolderDto[];
  unfiledCount: number;
  totalCount: number;
}

export interface CreateSourceFolderRequest {
  name: string;
  parentId?: Guid | null;
}

export interface MoveSourcesResponse {
  moved: number;
}

export interface SourceStatsDto {
  total: number;
  ready: number;
  pending: number;
  processing: number;
  failed: number;
  waiting: number;
  readyChunks: number;
  readyByType: Record<string, number>;
  readyByEmbeddingModel: Record<string, number>;
}

export interface AddUrlSourceRequest {
  url: string;
  title?: string | null;
  folderId?: Guid | null;
  scope?: CrawlScope | null;
  maxPages?: number | null;
  exclude?: string[] | null;
}

export interface AddTextSourceRequest {
  title: string;
  content: string;
  format?: 'text' | 'markdown' | null;
  folderId?: Guid | null;
}

export interface ChunkDto {
  chunkId: Guid;
  chunkIndex: number;
  content: string;
  page: number | null;
  metadata: Record<string, string>;
}

export type SearchMode = 'hybrid' | 'vector' | 'keyword';

export interface SearchRequest {
  query: string;
  topK?: number | null;
  sourceIds?: Guid[] | null;
  minScore?: number | null;
  mode?: SearchMode | null;
  rerank?: boolean | null;
}

export interface SearchHitDto {
  chunkId: Guid;
  sourceId: Guid;
  chunkIndex: number;
  sourceTitle: string;
  sourceUrl: string | null;
  page: number | null;
  content: string;
  score: number;
  vectorScore?: number | null;
  vectorRank?: number | null;
  keywordScore?: number | null;
  keywordRank?: number | null;
  rerankScore?: number | null;
}

export interface ReindexResponse {
  queued: number;
}
