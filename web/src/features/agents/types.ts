import type { Guid, IsoDateTime } from '../../lib/types';
import type { McpToolPolicy } from '../tools/mcp';

type McpAppMode = 'all' | 'chosen';

export type McpChangesPolicy = 'confirm' | 'verifiedOnly';

export interface AgentMcpAppDto {
  serverId: Guid;
  enabled: boolean;
  mode: McpAppMode;
  changesPolicy: McpChangesPolicy;
  tools: AgentMcpToolDto[];
}

export interface AgentMcpToolDto {
  name: string;
  policy: McpToolPolicy | null;
  fingerprint?: string | null;
}

export type HandoverPolicy = 'never' | 'onRequest' | 'whenUnsure';

export interface AgentSpecialistDto {
  agentId: Guid;
  whenToUse: string;
}

export interface AgentDto {
  id: Guid;
  key: string;
  name: string;
  description: string | null;
  instructions: string | null;
  language: string | null;
  profileName: string;
  folderIds: Guid[];
  tools: string[];
  allowGeneralKnowledge: boolean;
  handoverPolicy: HandoverPolicy;
  createdAt: IsoDateTime;
  updatedAt: IsoDateTime;
  mcpApps?: AgentMcpAppDto[];
  mcpServerEnabled?: boolean;
  httpApiEnabled?: boolean;
  specialists?: AgentSpecialistDto[];
  showSpecialistName?: boolean;
}

export interface AgentOptionsDto {
  tools: string[];
  profiles: string[];
}

export interface SaveAgentRequest {
  name: string;
  key?: string;
  description: string | null;
  instructions: string | null;
  language: string | null;
  profileName: string | null;
  folderIds: Guid[];
  tools: string[];
  allowGeneralKnowledge: boolean;
  handoverPolicy: HandoverPolicy;
  mcpApps?: AgentMcpAppDto[];
  specialists?: AgentSpecialistDto[];
  showSpecialistName?: boolean;
}

export interface AgentReferenceDto {
  id: Guid;
  name: string;
}
