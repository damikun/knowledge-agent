import type { Guid, IsoDateTime } from '../../lib/types';
import type { McpToolPolicy } from '../tools/mcp';

/** Which tools of a connected app an agent uses: all of them (also ones the app adds later) or only the picked ones. */
type McpAppMode = 'all' | 'chosen';

/** What tools that make changes need: the person confirms, or only verified visitors (who confirm too). */
export type McpChangesPolicy = 'confirm' | 'verifiedOnly';

/**
 * `all`: read-only tools run on their own, the rest follow `changesPolicy`.
 * `chosen`: only `tools`, each with its own policy (null for the tool's default).
 */
export interface AgentMcpAppDto {
  serverId: Guid;
  enabled: boolean;
  mode: McpAppMode;
  changesPolicy: McpChangesPolicy;
  tools: AgentMcpToolDto[];
}

/** fingerprint: the definition an admin approved; null when never approved. Only approved tools may run on their own. */
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

/** GET /api/agents. The built-in agent comes first. */
export interface AgentDto {
  id: Guid;
  /** Lowercase letters, digits and dashes; cannot change once created. */
  key: string;
  name: string;
  description: string | null;
  /** Persona and rules, added to the system prompt. */
  instructions: string | null;
  /** The language to answer in; null answers in the language of the question. */
  language: string | null;
  profileName: string;
  /** Empty means every source; otherwise the sources in these folders and their subfolders. */
  folderIds: Guid[];
  tools: string[];
  allowGeneralKnowledge: boolean;
  handoverPolicy: HandoverPolicy;
  isBuiltIn: boolean;
  createdAt: IsoDateTime;
  updatedAt: IsoDateTime;
  mcpApps?: AgentMcpAppDto[];
  mcpServerEnabled?: boolean;
  httpApiEnabled?: boolean;
  /** Agents it hands conversations to; a specialist only hands back. */
  specialists?: AgentSpecialistDto[];
  /** Whether the person sees the specialist's name ("Billing joined"); otherwise they see one assistant. */
  showSpecialistName?: boolean;
}

export interface AgentOptionsDto {
  tools: string[];
  profiles: string[];
}

/** Body of POST and PUT /api/agents. PUT replaces the whole agent, so every field is sent. */
export interface SaveAgentRequest {
  name: string;
  /** Only read on create. */
  key?: string;
  description: string | null;
  instructions: string | null;
  language: string | null;
  profileName: string | null;
  folderIds: Guid[];
  tools: string[];
  allowGeneralKnowledge: boolean;
  handoverPolicy: HandoverPolicy;
  /** Left out keeps the agent's own. */
  mcpApps?: AgentMcpAppDto[];
  /** Left out keeps the agent's own. */
  specialists?: AgentSpecialistDto[];
  showSpecialistName?: boolean;
}

/** 409 of DELETE /api/agents/{id}: the agents that hand conversations to it. */
export interface AgentReferenceDto {
  id: Guid;
  name: string;
}
