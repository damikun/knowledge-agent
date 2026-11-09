import { useQuery } from '@tanstack/react-query';
import { asArray, isRecord, request } from '../../../lib/api';
import type { Guid } from '../../../lib/types';
import type { McpAuthType, McpServerDto, McpTestResultDto, McpToolDto, McpToolPolicy, SaveMcpServerRequest } from './types';

export const mcpKeys = {
  all: ['connections', 'mcp'] as const,
  list: ['connections', 'mcp', 'list'] as const,
};

const AUTH_TYPES: readonly McpAuthType[] = ['none', 'bearer', 'header'];
const POLICIES: readonly McpToolPolicy[] = ['auto', 'confirm', 'verifiedOnly'];

function text(value: unknown): string | null {
  return typeof value === 'string' && value.trim() ? value : null;
}

function hint(value: unknown): boolean | null {
  return typeof value === 'boolean' ? value : null;
}

export function toPolicy(value: unknown, fallback: McpToolPolicy = 'confirm'): McpToolPolicy {
  return POLICIES.find((policy) => policy === value) ?? fallback;
}

function toTool(value: unknown): McpToolDto {
  const tool = isRecord(value) ? value : {};
  const readOnly = hint(tool.readOnly);
  return {
    name: text(tool.name) ?? '',
    modelName: text(tool.modelName) ?? '',
    title: text(tool.title),
    description: text(tool.description),
    inputSchema: (tool.inputSchema as McpToolDto['inputSchema']) ?? null,
    readOnly,
    destructive: hint(tool.destructive),
    idempotent: hint(tool.idempotent),
    openWorld: hint(tool.openWorld),
    defaultPolicy: toPolicy(tool.defaultPolicy, readOnly ? 'auto' : 'confirm'),
    fingerprint: text(tool.fingerprint) ?? '',
  };
}

function toServer(value: unknown): McpServerDto {
  const server = isRecord(value) ? value : {};
  const check = isRecord(server.lastCheck) ? server.lastCheck : null;
  return {
    id: text(server.id) ?? '',
    key: text(server.key) ?? '',
    name: text(server.name) ?? '',
    url: text(server.url) ?? '',
    authType: AUTH_TYPES.find((type) => type === server.authType) ?? 'none',
    headerName: text(server.headerName),
    hasSecret: server.hasSecret === true,
    secretHint: text(server.secretHint),
    enabled: server.enabled !== false,
    sendIdentity: server.sendIdentity === true,
    lastCheck: check ? { checkedAt: text(check.checkedAt) ?? '', ok: check.ok === true, error: text(check.error) } : null,
    serverName: text(server.serverName),
    serverVersion: text(server.serverVersion),
    tools: asArray<unknown>(server.tools)
      .map(toTool)
      .filter((tool) => tool.name),
    usedBy: asArray<Record<string, unknown>>(server.usedBy).map((use) => ({
      agentId: text(use.agentId) ?? '',
      agentName: text(use.agentName) ?? '',
      toolCount: typeof use.toolCount === 'number' ? use.toolCount : 0,
      mode: use.mode === 'chosen' ? ('chosen' as const) : ('all' as const),
    })),
    createdAt: text(server.createdAt) ?? '',
    updatedAt: text(server.updatedAt) ?? '',
  };
}

function toTestResult(value: unknown): McpTestResultDto {
  const result = isRecord(value) ? value : {};
  return {
    ok: result.ok === true,
    message: text(result.message) ?? '',
    serverName: text(result.serverName),
    serverVersion: text(result.serverVersion),
    tools: asArray<unknown>(result.tools).map(toTool),
    durationMs: typeof result.durationMs === 'number' ? result.durationMs : 0,
    checkedAt: text(result.checkedAt) ?? '',
  };
}

const serverPath = (id: Guid) => `/api/connections/mcp/${encodeURIComponent(id)}`;

export const mcpApi = {
  list: async (signal?: AbortSignal) => asArray<unknown>(await request<unknown>('/api/connections/mcp', { signal })).map(toServer),
  create: async (body: SaveMcpServerRequest) => toServer(await request<unknown>('/api/connections/mcp', { method: 'POST', body })),
  update: async (id: Guid, body: SaveMcpServerRequest) => toServer(await request<unknown>(serverPath(id), { method: 'PATCH', body })),
  remove: (id: Guid) => request<void>(serverPath(id), { method: 'DELETE' }),
  /** Tests the saved settings and refreshes the tools. */
  test: async (id: Guid) => toTestResult(await request<unknown>(`${serverPath(id)}/test`, { method: 'POST' })),
  /** Tests unsaved settings. */
  testDraft: async (body: SaveMcpServerRequest) => toTestResult(await request<unknown>('/api/connections/mcp/test', { method: 'POST', body })),
};

export function useMcpServers(options: { enabled?: boolean } = {}) {
  return useQuery({
    queryKey: mcpKeys.list,
    queryFn: ({ signal }) => mcpApi.list(signal),
    staleTime: 30_000,
    retry: false,
    enabled: options.enabled ?? true,
  });
}
