import type { Guid, IsoDateTime, JsonValue } from '../../../lib/types';

export type McpAuthType = 'none' | 'bearer' | 'header';

export type McpToolPolicy = 'auto' | 'confirm' | 'verifiedOnly';

export interface McpToolDto {
  name: string;
  modelName: string;
  title: string | null;
  description: string | null;
  inputSchema: JsonValue | null;
  readOnly: boolean | null;
  destructive: boolean | null;
  idempotent: boolean | null;
  openWorld: boolean | null;
  defaultPolicy: McpToolPolicy;
  fingerprint: string;
}

interface McpCheckDto {
  checkedAt: IsoDateTime;
  ok: boolean;
  error: string | null;
}

export interface McpServerDto {
  id: Guid;
  key: string;
  name: string;
  url: string;
  authType: McpAuthType;
  headerName: string | null;
  hasSecret: boolean;
  secretHint: string | null;
  enabled: boolean;
  sendIdentity: boolean;
  lastCheck: McpCheckDto | null;
  serverName: string | null;
  serverVersion: string | null;
  tools: McpToolDto[];
  usedBy: Array<{ agentId: Guid; agentName: string; toolCount: number; mode?: 'all' | 'chosen' }>;
  createdAt: IsoDateTime;
  updatedAt: IsoDateTime;
}

export interface SaveMcpServerRequest {
  name: string;
  url: string;
  authType: McpAuthType;
  headerName: string | null;
  secret: string | null;
  enabled?: boolean;
  sendIdentity?: boolean;
}

export interface McpTestResultDto {
  ok: boolean;
  message: string;
  serverName: string | null;
  serverVersion: string | null;
  tools: McpToolDto[];
  durationMs: number;
  checkedAt: IsoDateTime;
}
