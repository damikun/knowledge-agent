import type { StoreApi } from 'zustand/vanilla';

export type ReadonlyStore<T> = Pick<StoreApi<T>, 'getState' | 'getInitialState' | 'subscribe'>;
