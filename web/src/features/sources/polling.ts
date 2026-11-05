import type { SourceDto, SourceStatsDto } from './types';

export const SOURCE_POLL_INTERVAL_MS = 2000;

type IngestionCounts = Pick<SourceStatsDto, 'pending' | 'processing'>;

export function ingestingCount(stats: IngestionCounts | undefined): number {
  return stats ? Math.max(0, stats.pending) + Math.max(0, stats.processing) : 0;
}

export function hasActiveIngestion(stats: IngestionCounts | undefined): boolean {
  return ingestingCount(stats) > 0;
}

export function anyIngesting(sources: readonly Pick<SourceDto, 'status'>[] | undefined): boolean {
  return Boolean(sources?.some((source) => ['pending', 'processing'].includes(source.status?.toLowerCase())));
}

export function sourcesRefetchInterval(stats: IngestionCounts | undefined): number | false {
  return hasActiveIngestion(stats) ? SOURCE_POLL_INTERVAL_MS : false;
}
