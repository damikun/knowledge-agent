import { useQueries, useQueryClient, type QueryClient } from '@tanstack/react-query';
import { sourceKeys, sourcesApi } from './api';

/** The most ids `GET /api/sources?ids=` accepts in one request. */
const SOURCE_IDS_PER_REQUEST = 100;
const TITLE_STALE_MS = 5 * 60_000;

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/** Ids invented by a model (e.g. "handbook") are never sent to the API. */
export function isSourceId(value: string): boolean {
  return GUID.test(value);
}

interface Waiter {
  resolve: (title: string | null) => void;
  reject: (error: unknown) => void;
}

/**
 * Collects the ids requested within one tick (e.g. by every tool card of a conversation) and loads them with one
 * request per 100 ids. Missing sources resolve to null.
 */
class SourceTitleBatcher {
  #queue = new Map<string, Waiter[]>();
  #scheduled = false;

  load(id: string): Promise<string | null> {
    return new Promise((resolve, reject) => {
      const waiters = this.#queue.get(id);
      if (waiters) {
        waiters.push({ resolve, reject });
      } else {
        this.#queue.set(id, [{ resolve, reject }]);
      }
      if (!this.#scheduled) {
        this.#scheduled = true;
        setTimeout(() => void this.#flush(), 0);
      }
    });
  }

  async #flush(): Promise<void> {
    const batch = this.#queue;
    this.#queue = new Map();
    this.#scheduled = false;
    const ids = [...batch.keys()];
    const requests: Promise<void>[] = [];
    for (let start = 0; start < ids.length; start += SOURCE_IDS_PER_REQUEST) {
      const chunk = ids.slice(start, start + SOURCE_IDS_PER_REQUEST);
      requests.push(
        sourcesApi.listSources({ ids: chunk, limit: SOURCE_IDS_PER_REQUEST }).then(
          (page) => {
            const titles = new Map(page.items.map((source) => [source.id.toLowerCase(), source.title || null] as const));
            for (const id of chunk) {
              batch.get(id)?.forEach((waiter) => waiter.resolve(titles.get(id) ?? null));
            }
          },
          (error: unknown) => {
            for (const id of chunk) {
              batch.get(id)?.forEach((waiter) => waiter.reject(error));
            }
          },
        ),
      );
    }
    await Promise.all(requests);
  }
}

const batchers = new WeakMap<QueryClient, SourceTitleBatcher>();

function batcherFor(queryClient: QueryClient): SourceTitleBatcher {
  let batcher = batchers.get(queryClient);
  if (!batcher) {
    batcher = new SourceTitleBatcher();
    batchers.set(queryClient, batcher);
  }
  return batcher;
}

/**
 * Resolves source ids to titles, caching each id separately. Returns undefined for ids that are unknown, deleted or
 * still loading, so callers can fall back to the id.
 */
export function useSourceTitles(ids: readonly string[]): (id: string) => string | undefined {
  const queryClient = useQueryClient();
  const wanted = [...new Set(ids.map((id) => id.trim().toLowerCase()).filter(isSourceId))];
  const results = useQueries({
    queries: wanted.map((id) => ({
      queryKey: sourceKeys.title(id),
      queryFn: () => batcherFor(queryClient).load(id),
      staleTime: TITLE_STALE_MS,
    })),
  });
  const titles = new Map<string, string>();
  wanted.forEach((id, index) => {
    const title = results[index]?.data;
    if (title) {
      titles.set(id, title);
    }
  });
  return (id: string) => titles.get(id.trim().toLowerCase());
}
