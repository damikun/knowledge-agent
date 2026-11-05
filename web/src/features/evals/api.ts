import { useQuery } from '@tanstack/react-query';
import { asArray, request } from '../../lib/api';
import type { Guid } from '../../lib/types';
import { evalRunRefetchInterval, evalRunsRefetchInterval } from './polling';
import type {
  CreateEvalDatasetRequest,
  EvalDatasetDto,
  EvalDatasetItemDto,
  EvalDatasetListDto,
  EvalDatasetSyncResultDto,
  EvalRunDto,
  EvalRunListParams,
  EvalRunSummaryDto,
  ImportEvalItemsResultDto,
  SaveEvalItemRequest,
  StartEvalRunRequest,
} from './types';

export const evalKeys = {
  datasets: ['evals', 'datasets'] as const,
  items: (id: string) => ['evals', 'datasets', id, 'items'] as const,
  runs: ['evals', 'runs'] as const,
  runLists: ['evals', 'runs', 'list'] as const,
  runList: (params: EvalRunListParams) => ['evals', 'runs', 'list', normalizeEvalRunListParams(params)] as const,
  run: (id: string) => ['evals', 'runs', 'detail', id] as const,
  sourcePicker: (words: string) => ['evals', 'source-picker', words.toLowerCase()] as const,
  conversationPicker: (filters: { show: string; labelId: string; period: string; words: string }) => ['evals', 'conversation-picker', filters] as const,
};

export function normalizeEvalRunListParams(params: EvalRunListParams = {}): EvalRunListParams {
  const result: { -readonly [K in keyof EvalRunListParams]: EvalRunListParams[K] } = {};
  if (params.limit != null) {
    result.limit = params.limit;
  }
  const dataset = params.dataset?.trim();
  if (dataset) {
    result.dataset = dataset;
  }
  return result;
}

/** Older APIs omit warnings. */
function normalizeEvalRun(run: EvalRunDto): EvalRunDto {
  return {
    ...run,
    byTag: Array.isArray(run.byTag) ? run.byTag : [],
    items: Array.isArray(run.items) ? run.items : [],
    warnings: Array.isArray(run.warnings) ? run.warnings.filter((w): w is string => typeof w === 'string' && w.trim() !== '') : [],
  };
}

/** Fills fields older APIs leave out. */
function normalizeItem(item: EvalDatasetItemDto): EvalDatasetItemDto {
  return {
    ...item,
    source: item.source ?? (item.conversationId ? 'conversation' : 'manual'),
    history: Array.isArray(item.history) ? item.history : [],
  };
}

const datasetPath = (id: Guid) => `/api/evals/datasets/${encodeURIComponent(id)}`;

export const evalsApi = {
  listEvalDatasets: (signal?: AbortSignal) => request<EvalDatasetListDto>('/api/evals/datasets', { signal }),
  /** 400 when Langfuse is not configured. */
  syncEvalDataset: (name: string) => request<EvalDatasetSyncResultDto>(`/api/evals/datasets/${encodeURIComponent(name)}/sync`, { method: 'POST' }),
  createDataset: (body: CreateEvalDatasetRequest) => request<EvalDatasetDto>('/api/evals/datasets', { method: 'POST', body }),
  updateDataset: (id: Guid, body: { name?: string; description?: string }) =>
    request<EvalDatasetDto>(`${datasetPath(id)}`, { method: 'PATCH', body }),
  deleteDataset: (id: Guid) => request<void>(datasetPath(id), { method: 'DELETE' }),
  /** 400 when Langfuse is not configured. */
  syncCustomDataset: (id: Guid) => request<EvalDatasetSyncResultDto>(`${datasetPath(id)}/sync`, { method: 'POST' }),
  listItems: async (id: Guid, signal?: AbortSignal) =>
    asArray<EvalDatasetItemDto>(await request<unknown>(`${datasetPath(id)}/items`, { signal })).map(normalizeItem),
  /** Up to 1000 questions in one transaction; questions the dataset has are skipped. */
  importItems: (id: Guid, items: SaveEvalItemRequest[]) =>
    request<ImportEvalItemsResultDto>(`${datasetPath(id)}/items/batch`, { method: 'POST', body: { items } }),
  deleteItems: (id: Guid, ids: Guid[]) => request<{ deleted: number }>(`${datasetPath(id)}/items/delete`, { method: 'POST', body: { ids } }),
  addItem: (id: Guid, body: SaveEvalItemRequest) => request<EvalDatasetItemDto>(`${datasetPath(id)}/items`, { method: 'POST', body }),
  updateItem: (id: Guid, itemId: Guid, body: SaveEvalItemRequest) =>
    request<EvalDatasetItemDto>(`${datasetPath(id)}/items/${encodeURIComponent(itemId)}`, { method: 'PUT', body }),
  deleteItem: (id: Guid, itemId: Guid) => request<void>(`${datasetPath(id)}/items/${encodeURIComponent(itemId)}`, { method: 'DELETE' }),
  /** Newest first. */
  listEvalRuns: async (params: EvalRunListParams = {}, signal?: AbortSignal) => {
    const normalized = normalizeEvalRunListParams(params);
    return asArray<EvalRunSummaryDto>(
      await request<unknown>('/api/evals/runs', { query: { limit: normalized.limit, dataset: normalized.dataset }, signal }),
    );
  },
  startEvalRun: (body: StartEvalRunRequest) => request<EvalRunSummaryDto | undefined>('/api/evals/runs', { method: 'POST', body }),
  getEvalRun: async (id: Guid, signal?: AbortSignal) =>
    normalizeEvalRun(await request<EvalRunDto>(`/api/evals/runs/${encodeURIComponent(id)}`, { signal })),
  cancelEvalRun: (id: Guid) => request<EvalRunSummaryDto | undefined>(`/api/evals/runs/${encodeURIComponent(id)}/cancel`, { method: 'POST' }),
  /** 409 while the run is queued or running. */
  deleteEvalRun: (id: Guid) => request<void>(`/api/evals/runs/${encodeURIComponent(id)}`, { method: 'DELETE' }),
};

export function useEvalDatasets() {
  return useQuery({ queryKey: evalKeys.datasets, queryFn: ({ signal }) => evalsApi.listEvalDatasets(signal), staleTime: 30_000 });
}

export function useEvalDatasetItems(id: string | null) {
  return useQuery({ queryKey: evalKeys.items(id ?? ''), queryFn: ({ signal }) => evalsApi.listItems(id!, signal), enabled: Boolean(id) });
}

export interface EvalRunsOptions {
  enabled?: boolean;
  /** Refetch while a listed run is queued or running (default true). */
  poll?: boolean;
}

export function useEvalRuns(params: EvalRunListParams = {}, options: EvalRunsOptions = {}) {
  const normalized = normalizeEvalRunListParams(params);
  const poll = options.poll ?? true;
  return useQuery({
    queryKey: evalKeys.runList(normalized),
    queryFn: ({ signal }) => evalsApi.listEvalRuns(normalized, signal),
    enabled: options.enabled ?? true,
    refetchInterval: poll ? (query) => evalRunsRefetchInterval(query.state.data) : false,
  });
}

export function useEvalRun(id: string | null) {
  return useQuery({
    queryKey: evalKeys.run(id ?? ''),
    queryFn: ({ signal }) => evalsApi.getEvalRun(id!, signal),
    enabled: Boolean(id),
    refetchInterval: (query) => evalRunRefetchInterval(query.state.data),
  });
}
