import type { Guid, IsoDateTime } from '../../lib/types';

/** website: a crawled site; each page it finds is a url source with the website as its parent. */
export type SourceType = 'url' | 'pdf' | 'text' | 'markdown' | 'website';
/**
 * waiting: no embedding model is configured; the API queues the source again once one is saved.
 * needsbrowser: a crawled page with almost no text until JavaScript runs, so nothing was indexed.
 */
export type SourceStatus = 'pending' | 'processing' | 'ready' | 'failed' | 'waiting' | 'needsbrowser';

/** page: only the URL; section: the page and the pages under its path; site: the whole host. */
export type CrawlScope = 'page' | 'section' | 'site';

export interface WebsiteCrawlDto {
  scope: CrawlScope;
  maxPages: number;
  exclude: string[];
  /** Progress of the current or last crawl. */
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
  /** Host of a web page without "www.", for grouping pages of one site. */
  site: string | null;
  fileName: string | null;
  contentType: string | null;
  sizeBytes: number | null;
  /** While embedding or storing, the total of the current run. */
  chunkCount: number;
  pageCount: number | null;
  tokenEstimate: number;
  /** `{providerType}:{model}:{dimensions}` of the stored vectors. */
  embeddingModel: string | null;
  error: string | null;
  attempts: number;
  /** Set only while the status is processing. */
  stage: IngestionStage | null;
  /** Chunks embedded so far in the current run. */
  embeddedChunks: number;
  /** The last job run that processed the source. */
  jobRunId: string | null;
  /** That run in the Inngest dashboard; null when the jobs don't run on Inngest. */
  jobRunUrl: string | null;
  createdAt: IsoDateTime;
  updatedAt: IsoDateTime;
  processedAt: IsoDateTime | null;
  metadata: Record<string, string>;
  folderId: Guid | null;
  /** The website a crawled page belongs to. */
  parentId?: Guid | null;
  /** Set on websites only. */
  crawl?: WebsiteCrawlDto | null;
  /** When the crawler last fetched or revalidated a page. */
  lastFetchedAt?: IsoDateTime | null;
}

export interface SourcePageDto {
  items: SourceDto[];
  /** All sources matching the filters, not just this page. */
  total: number;
  offset: number;
  /** The page size that was applied, after clamping. */
  limit: number;
  /** Null on the last page. */
  nextOffset: number | null;
}

/** Query parameters of `GET /api/sources`; empty values are not sent. */
export interface SourceListParams {
  /** Every word must appear in the title, URL or file name (case-insensitive). */
  query?: string | null;
  type?: SourceType | null;
  /** Any of these statuses; empty for all. */
  status?: readonly SourceStatus[] | null;
  /** At most 100. */
  ids?: readonly Guid[] | null;
  offset?: number | null;
  /** 1..100, default 25. */
  limit?: number | null;
  /** Sources directly in this folder, or 'unfiled' for sources in no folder; empty for all. */
  folder?: Guid | 'unfiled' | null;
  site?: string | null;
  /** A website's pages; without it the list leaves crawled pages out. */
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
  /** Sources directly in this folder, not in its subfolders. */
  sourceCount: number;
  createdAt: IsoDateTime;
  updatedAt: IsoDateTime;
  /**
   * The built-in Internal folder: only the help assistant searches what is in it and its subfolders, never an agent. It
   * cannot be renamed, moved or deleted. Set on that one folder only, never on its subfolders.
   */
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
  /** Sources waiting for an embedding model. */
  waiting: number;
  readyChunks: number;
  readyByType: Record<string, number>;
  /** Keyed by `{providerType}:{model}:{dimensions}`. */
  readyByEmbeddingModel: Record<string, number>;
}

export interface AddUrlSourceRequest {
  url: string;
  title?: string | null;
  folderId?: Guid | null;
  /** Defaults to page. */
  scope?: CrawlScope | null;
  /** For section and site; defaults to 100. */
  maxPages?: number | null;
  /** Path patterns to skip; * matches anything. */
  exclude?: string[] | null;
}

export interface AddTextSourceRequest {
  title: string;
  content: string;
  /** Defaults to text. */
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

/** hybrid: vector + keyword fused by rank; vector: embeddings only; keyword: BM25 term matching only. */
export type SearchMode = 'hybrid' | 'vector' | 'keyword';

export interface SearchRequest {
  query: string;
  topK?: number | null;
  sourceIds?: Guid[] | null;
  /** Filters vector candidates by cosine similarity; keyword candidates are not cut. */
  minScore?: number | null;
  /** Null uses Search:Mode (hybrid by default). */
  mode?: SearchMode | null;
  /** Rerank with the "rerank" profile; null uses Search:Rerank. */
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
  /** Rerank relevance when reranked, else the fused score (hybrid), cosine (vector) or BM25 relative to the best hit (keyword). */
  score: number;
  /** Cosine similarity, when vector search found the chunk. */
  vectorScore?: number | null;
  /** 1-based. */
  vectorRank?: number | null;
  /** BM25 score, when keyword search found the chunk. */
  keywordScore?: number | null;
  /** 1-based. */
  keywordRank?: number | null;
  /** Relevance 0..1 from the reranker. */
  rerankScore?: number | null;
}

export interface ReindexResponse {
  queued: number;
}
