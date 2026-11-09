import type { Guid, IsoDateTime, JsonValue } from '../../../lib/types';

/** none, `Authorization: Bearer <token>`, or a custom header. */
export type McpAuthType = 'none' | 'bearer' | 'header';

/** confirm: after the person confirms; verifiedOnly: only for verified visitors, who confirm too. */
export type McpToolPolicy = 'auto' | 'confirm' | 'verifiedOnly';

/** The hints are the server's own claims; null when it said nothing. */
export interface McpToolDto {
  name: string;
  /** mcp_<server key>__<tool>. */
  modelName: string;
  title: string | null;
  description: string | null;
  inputSchema: JsonValue | null;
  readOnly: boolean | null;
  destructive: boolean | null;
  idempotent: boolean | null;
  openWorld: boolean | null;
  /** auto for tools marked read-only, confirm for the rest. */
  defaultPolicy: McpToolPolicy;
  /** Of the current definition; an agent keeps the one it approved. */
  fingerprint: string;
}

interface McpCheckDto {
  checkedAt: IsoDateTime;
  ok: boolean;
  error: string | null;
}

/** The secret is never returned, only whether one is saved and its last characters. */
export interface McpServerDto {
  id: Guid;
  /** Part of the tool names, so it never changes. */
  key: string;
  name: string;
  url: string;
  authType: McpAuthType;
  headerName: string | null;
  hasSecret: boolean;
  secretHint: string | null;
  enabled: boolean;
  /** Each tool call carries the verified user's ID in its _meta. */
  sendIdentity: boolean;
  lastCheck: McpCheckDto | null;
  serverName: string | null;
  serverVersion: string | null;
  tools: McpToolDto[];
  /** toolCount: how many tools each agent gets; mode: whether that follows the server. */
  usedBy: Array<{ agentId: Guid; agentName: string; toolCount: number; mode?: 'all' | 'chosen' }>;
  createdAt: IsoDateTime;
  updatedAt: IsoDateTime;
}

/** secret: null keeps the saved one, "" removes it. */
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
