import { useQuery } from '@tanstack/react-query';
import { asArray, call, isRecord } from '../../lib/api';
import { endpoints } from '../../lib/endpoints';
import type { Guid, IsoDateTime } from '../../lib/types';

export interface AgentAccessDto {
  mcpServerEnabled: boolean;
  httpApiEnabled: boolean;
  mcpPath: string;
  apiPath: string;
  activeKeys: number;
}

export interface AgentApiKeyDto {
  id: Guid;
  name: string;
  prefix: string;
  requestsPerMinute: number;
  createdBy: string | null;
  createdAt: IsoDateTime;
  lastUsedAt: IsoDateTime | null;
  revokedAt: IsoDateTime | null;
}

export interface CreatedAgentApiKeyDto {
  key: AgentApiKeyDto;
  secret: string;
}

export const accessKeys = {
  access: (agentId: Guid) => ['agents', agentId, 'access'] as const,
  keys: (agentId: Guid) => ['agents', agentId, 'api-keys'] as const,
};

function text(value: unknown): string | null {
  return typeof value === 'string' && value.trim() ? value : null;
}

function toAccess(value: unknown): AgentAccessDto {
  const access = isRecord(value) ? value : {};
  return {
    mcpServerEnabled: access.mcpServerEnabled === true,
    httpApiEnabled: access.httpApiEnabled === true,
    mcpPath: text(access.mcpPath) ?? '',
    apiPath: text(access.apiPath) ?? '',
    activeKeys: typeof access.activeKeys === 'number' ? access.activeKeys : 0,
  };
}

function toKey(value: unknown): AgentApiKeyDto {
  const key = isRecord(value) ? value : {};
  return {
    id: text(key.id) ?? '',
    name: text(key.name) ?? '',
    prefix: text(key.prefix) ?? '',
    requestsPerMinute: typeof key.requestsPerMinute === 'number' ? key.requestsPerMinute : 0,
    createdBy: text(key.createdBy),
    createdAt: text(key.createdAt) ?? '',
    lastUsedAt: text(key.lastUsedAt),
    revokedAt: text(key.revokedAt),
  };
}

export const accessApi = {
  getAccess: async (agentId: Guid, signal?: AbortSignal) => toAccess(await call(endpoints.agents.access, { params: { id: agentId }, signal })),
  saveAccess: async (agentId: Guid, body: { mcpServerEnabled?: boolean; httpApiEnabled?: boolean }) =>
    toAccess(await call(endpoints.agents.saveAccess, { params: { id: agentId }, body })),
  listKeys: async (agentId: Guid, signal?: AbortSignal) =>
    asArray<unknown>(await call(endpoints.agents.listKeys, { params: { id: agentId }, signal })).map(toKey),
  createKey: async (agentId: Guid, body: { name: string; requestsPerMinute: number }) => {
    const created = await call(endpoints.agents.createKey, { params: { id: agentId }, body });
    const record = isRecord(created) ? created : {};
    return { key: toKey(record.key), secret: text(record.secret) ?? '' } satisfies CreatedAgentApiKeyDto;
  },
  revokeKey: (agentId: Guid, keyId: Guid): Promise<void> => call(endpoints.agents.revokeKey, { params: { id: agentId, keyId } }),
};

export function useAgentAccess(agentId: Guid) {
  return useQuery({ queryKey: accessKeys.access(agentId), queryFn: ({ signal }) => accessApi.getAccess(agentId, signal), staleTime: 30_000 });
}

export function useAgentApiKeys(agentId: Guid) {
  return useQuery({ queryKey: accessKeys.keys(agentId), queryFn: ({ signal }) => accessApi.listKeys(agentId, signal), staleTime: 30_000 });
}

export function mcpSnippets(name: string, url: string, key: string) {
  const id = name.replace(/[^a-z0-9-]/gi, '-').toLowerCase() || 'agent';
  return {
    claudeDesktop: JSON.stringify(
      {
        mcpServers: {
          [id]: {
            command: 'npx',
            args: ['-y', 'mcp-remote', url, '--header', 'Authorization:${AUTH_HEADER}'],
            env: { AUTH_HEADER: `Bearer ${key}` },
          },
        },
      },
      null,
      2,
    ),
    cursor: JSON.stringify({ mcpServers: { [id]: { url, headers: { Authorization: `Bearer ${key}` } } } }, null, 2),
    generic: `URL: ${url}\nTransport: Streamable HTTP\nHeader: Authorization: Bearer ${key}\nTools: ask, search_knowledge`,
  };
}

export function apiSnippet(url: string, key: string): string {
  return `curl -X POST ${url}/ask \\\n  -H "Authorization: Bearer ${key}" \\\n  -H "Content-Type: application/json" \\\n  -d '{"question": "How long can I return an order?"}'`;
}
