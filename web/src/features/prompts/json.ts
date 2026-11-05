import type { JsonObject } from '../../lib/types';

export type JsonParseResult = { ok: true; value: JsonObject } | { ok: false; error: string };

/** Must be a JSON object; empty text means no variables. */
export function parseVariables(text: string): JsonParseResult {
  if (!text.trim()) {
    return { ok: true, value: {} };
  }
  try {
    const parsed: unknown = JSON.parse(text);
    if (typeof parsed !== 'object' || parsed === null || Array.isArray(parsed)) {
      return { ok: false, error: 'Variables must be a JSON object, e.g. { "name": "value" }.' };
    }
    return { ok: true, value: parsed as JsonObject };
  } catch (error) {
    return { ok: false, error: error instanceof Error ? error.message : 'Invalid JSON.' };
  }
}

export function formatJson(value: unknown): string {
  return JSON.stringify(value, null, 2);
}
