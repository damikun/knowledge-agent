import { ApiError } from '../../lib/api';
import { findEmbeddingProfile } from '../models/embedding';
import type { ModelProfileDto } from '../models/types';

export const ORCHESTRATOR_PROFILE = 'orchestrator';

export function findProfile(profiles: readonly ModelProfileDto[] | undefined, name: string): ModelProfileDto | undefined {
  return profiles?.find((p) => p.name.toLowerCase() === name.toLowerCase());
}

/** Null while profiles load or when chat and embeddings both resolve to a model. */
export function setupMessage(profiles: readonly ModelProfileDto[] | undefined): string | null {
  const orchestrator = findProfile(profiles, ORCHESTRATOR_PROFILE);
  const embedding = findEmbeddingProfile(profiles);
  const noChat = orchestrator !== undefined && !orchestrator.effective;
  const noEmbedding = embedding !== undefined && !embedding.effective;
  if (noChat && noEmbedding) {
    return 'Connect a chat model and an embedding model to use the agent.';
  }
  if (noChat) {
    return 'Connect a chat model to chat with the agent.';
  }
  return noEmbedding ? 'Connect an embedding model so sources can be ingested and searched.' : null;
}

export function isApiUnreachable(error: unknown): boolean {
  return error instanceof ApiError && (error.status === 0 || error.status === 502 || error.status === 503 || error.status === 504);
}

export function describeModel(profile: ModelProfileDto | undefined): string | null {
  const effective = profile?.effective;
  if (!effective) {
    return null;
  }
  return effective.model ? `${effective.providerKey} · ${effective.model}` : effective.providerKey;
}
