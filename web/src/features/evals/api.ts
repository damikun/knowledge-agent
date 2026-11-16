import { useQuery } from '@tanstack/react-query';
import { asArray, call } from '../../lib/api';
import { endpoints } from '../../lib/endpoints';
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

function normalizeEvalRun(run: EvalRunDto): EvalRunDto {
  return {
    ...run,
    byTag: Array.isArray(run.byTag) ? run.byTag : [],
    items: Array.isArray(run.items) ? run.items : [],
    warnings: Array.isArray(run.warnings) ? run.warnings.filter((w): w is string => typeof w === 'string' && w.trim() !== '') : [],
  };
}

function normalizeItem(item: EvalDatasetItemDto): EvalDatasetItemDto {
  return {
    ...item,
    source: item.source ?? (item.conversationId ? 'conversation' : 'manual'),
    history: Array.isArray(item.history) ? item.history : [],
  };
}

export const evalsApi = {
  listEvalDatasets: (signal?: AbortSignal): Promise<EvalDatasetListDto> => call(endpoints.evals.listDatasets, { signal }),
  syncEvalDataset: (name: string): Promise<EvalDatasetSyncResultDto> => call(endpoints.evals.syncNamedDataset, { params: { name } }),
  createDataset: (body: CreateEvalDatasetRequest): Promise<EvalDatasetDto> => call(endpoints.evals.createDataset, { body }),
  updateDataset: (id: Guid, body: { name?: string; description?: string }): Promise<EvalDatasetDto> =>
    call(endpoints.evals.updateDataset, { params: { id }, body }),
  deleteDataset: (id: Guid): Promise<void> => call(endpoints.evals.deleteDataset, { params: { id } }),
  syncCustomDataset: (id: Guid): Promise<EvalDatasetSyncResultDto> => call(endpoints.evals.syncDataset, { params: { id } }),
  listItems: async (id: Guid, signal?: AbortSignal) =>
    asArray<EvalDatasetItemDto>(await call(endpoints.evals.listItems, { params: { id }, signal })).map(normalizeItem),
  importItems: (id: Guid, items: SaveEvalItemRequest[]): Promise<ImportEvalItemsResultDto> =>
    call(endpoints.evals.importItems, { params: { id }, body: { items } }),
  deleteItems: (id: Guid, ids: Guid[]): Promise<{ deleted: number }> => call(endpoints.evals.deleteItems, { params: { id }, body: { ids } }),
  addItem: (id: Guid, body: SaveEvalItemRequest): Promise<EvalDatasetItemDto> => call(endpoints.evals.addItem, { params: { id }, body }),
  updateItem: (id: Guid, itemId: Guid, body: SaveEvalItemRequest): Promise<EvalDatasetItemDto> =>
    call(endpoints.evals.updateItem, { params: { id, itemId }, body }),
  deleteItem: (id: Guid, itemId: Guid): Promise<void> => call(endpoints.evals.deleteItem, { params: { id, itemId } }),
  listEvalRuns: async (params: EvalRunListParams = {}, signal?: AbortSignal) => {
    const normalized = normalizeEvalRunListParams(params);
    return asArray<EvalRunSummaryDto>(
      await call(endpoints.evals.listRuns, { query: { limit: normalized.limit, dataset: normalized.dataset }, signal }),
    );
  },
  startEvalRun: (body: StartEvalRunRequest): Promise<EvalRunSummaryDto | undefined> => call(endpoints.evals.startRun, { body }),
  getEvalRun: async (id: Guid, signal?: AbortSignal) =>
    normalizeEvalRun(await call(endpoints.evals.getRun, { params: { id }, signal })),
  cancelEvalRun: (id: Guid): Promise<EvalRunSummaryDto | undefined> => call(endpoints.evals.cancelRun, { params: { id } }),
  deleteEvalRun: (id: Guid): Promise<void> => call(endpoints.evals.deleteRun, { params: { id } }),
};

export function useEvalDatasets() {
  return useQuery({ queryKey: evalKeys.datasets, queryFn: ({ signal }) => evalsApi.listEvalDatasets(signal), staleTime: 30_000 });
}

export function useEvalDatasetItems(id: string | null) {
  return useQuery({ queryKey: evalKeys.items(id ?? ''), queryFn: ({ signal }) => evalsApi.listItems(id!, signal), enabled: Boolean(id) });
}

export interface EvalRunsOptions {
  enabled?: boolean;
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
