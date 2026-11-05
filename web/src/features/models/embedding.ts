import type { EffectiveModelDto, ModelProfileDto } from './types';

const EMBEDDING_PROFILE = 'embedding';

type EmbeddingModel = Pick<EffectiveModelDto, 'providerType' | 'model' | 'dimensions'>;

/** The key the API stores per chunk: `{providerType}:{model}:{dimensions}`. */
export function embeddingKey(effective: EmbeddingModel): string {
  return `${effective.providerType}:${effective.model}:${effective.dimensions ?? ''}`;
}

export function findEmbeddingProfile(profiles: readonly ModelProfileDto[] | undefined): ModelProfileDto | undefined {
  return profiles?.find((p) => p.name === EMBEDDING_PROFILE) ?? profiles?.find((p) => p.kind?.toLowerCase() === 'embedding');
}

/** Without reported dimensions (provider default), only provider type and model must match. */
export function isCurrentEmbedding(sourceKey: string, effective: EmbeddingModel): boolean {
  if (effective.dimensions !== null && effective.dimensions !== undefined) {
    return sourceKey === embeddingKey(effective);
  }
  return sourceKey.startsWith(`${effective.providerType}:${effective.model}:`);
}

export interface StaleEmbeddings {
  /** Ready sources embedded with a model other than the effective one. */
  count: number;
  /** Most used first. */
  models: string[];
}

/** From the source stats, so not every source has to be loaded. */
export function staleEmbeddings(
  readyByEmbeddingModel: Readonly<Record<string, number>> | undefined,
  profile: ModelProfileDto | undefined,
): StaleEmbeddings {
  const effective = profile?.effective;
  if (!readyByEmbeddingModel || !effective) {
    return { count: 0, models: [] };
  }
  const stale = Object.entries(readyByEmbeddingModel)
    .filter(([key, count]) => key.trim() !== '' && count > 0 && !isCurrentEmbedding(key, effective))
    .sort(([keyA, a], [keyB, b]) => b - a || keyA.localeCompare(keyB));
  return { count: stale.reduce((sum, [, count]) => sum + count, 0), models: stale.map(([key]) => key) };
}

export function staleEmbeddingSources<T extends { status: string; embeddingModel: string | null }>(
  sources: readonly T[] | undefined,
  profile: ModelProfileDto | undefined,
): T[] {
  const effective = profile?.effective;
  if (!sources || !effective) {
    return [];
  }
  return sources.filter(
    (s) => s.status?.toLowerCase() === 'ready' && Boolean(s.embeddingModel) && !isCurrentEmbedding(s.embeddingModel!, effective),
  );
}
