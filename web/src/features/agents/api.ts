import { useQuery } from '@tanstack/react-query';
import { ApiError, asArray, asStrings, isRecord, request } from '../../lib/api';
import type { Guid } from '../../lib/types';
import { toPolicy } from '../tools/mcp';
import type { AgentDto, AgentMcpAppDto, AgentOptionsDto, AgentReferenceDto, AgentSpecialistDto, HandoverPolicy, SaveAgentRequest } from './types';

export const agentKeys = {
  list: ['agents', 'list'] as const,
  options: ['agents', 'options'] as const,
};

const HANDOVER_POLICIES: readonly HandoverPolicy[] = ['never', 'onRequest', 'whenUnsure'];

function handoverPolicy(value: unknown): HandoverPolicy {
  return HANDOVER_POLICIES.find((policy) => policy === value) ?? 'onRequest';
}

function mcpApps(value: unknown): AgentMcpAppDto[] {
  return asArray<Record<string, unknown>>(value)
    .filter((set) => isRecord(set) && typeof set.serverId === 'string')
    .map((set) => ({
      serverId: set.serverId as string,
      enabled: set.enabled !== false,
      mode: set.mode === 'chosen' ? 'chosen' : 'all',
      changesPolicy: set.changesPolicy === 'verifiedOnly' ? 'verifiedOnly' : 'confirm',
      tools: asArray<Record<string, unknown>>(set.tools)
        .filter((tool) => isRecord(tool) && typeof tool.name === 'string')
        .map((tool) => ({
          name: tool.name as string,
          policy: tool.policy === undefined || tool.policy === null ? null : toPolicy(tool.policy),
          fingerprint: typeof tool.fingerprint === 'string' && tool.fingerprint ? tool.fingerprint : null,
        })),
    }));
}

function specialists(value: unknown): AgentSpecialistDto[] {
  return asArray<Record<string, unknown>>(value)
    .filter((item) => isRecord(item) && typeof item.agentId === 'string')
    .map((item) => ({ agentId: item.agentId as string, whenToUse: typeof item.whenToUse === 'string' ? item.whenToUse : '' }));
}

/** The agents named by a 409 of a delete: they hand conversations to the agent. */
export function teamsOf(error: unknown): AgentReferenceDto[] {
  if (!(error instanceof ApiError) || error.status !== 409) {
    return [];
  }
  return asArray<Record<string, unknown>>(error.problem?.agents)
    .filter((item) => isRecord(item) && typeof item.id === 'string' && typeof item.name === 'string')
    .map((item) => ({ id: item.id as string, name: item.name as string }));
}

function normalizeAgent(agent: AgentDto): AgentDto {
  return {
    ...agent,
    description: agent.description ?? null,
    instructions: agent.instructions ?? null,
    language: agent.language ?? null,
    folderIds: asStrings(agent.folderIds),
    tools: asStrings(agent.tools),
    allowGeneralKnowledge: agent.allowGeneralKnowledge === true,
    handoverPolicy: handoverPolicy(agent.handoverPolicy),
    isBuiltIn: agent.isBuiltIn === true,
    mcpApps: mcpApps(agent.mcpApps),
    mcpServerEnabled: agent.mcpServerEnabled === true,
    httpApiEnabled: agent.httpApiEnabled === true,
    specialists: specialists(agent.specialists),
    showSpecialistName: agent.showSpecialistName === true,
  };
}

function toOptions(value: unknown): AgentOptionsDto {
  const options = isRecord(value) ? value : {};
  return { tools: asStrings(options.tools), profiles: asStrings(options.profiles) };
}

const agentPath = (id: Guid) => `/api/agents/${encodeURIComponent(id)}`;

export const agentsApi = {
  /** Built-in agent last. Its row only names the agent of old conversations; lists and pickers use ownAgents. */
  listAgents: async (signal?: AbortSignal) =>
    asArray<AgentDto>(await request<unknown>('/api/agents', { signal }))
      .map(normalizeAgent)
      // Stable, so the API's order holds otherwise.
      .sort((a, b) => Number(a.isBuiltIn) - Number(b.isBuiltIn)),
  getOptions: async (signal?: AbortSignal) => toOptions(await request<unknown>('/api/agents/options', { signal })),
  createAgent: async (body: SaveAgentRequest) => {
    const agent = await request<AgentDto | undefined>('/api/agents', { method: 'POST', body });
    return agent ? normalizeAgent(agent) : undefined;
  },
  updateAgent: async (id: Guid, body: SaveAgentRequest) => {
    const agent = await request<AgentDto | undefined>(agentPath(id), { method: 'PUT', body });
    return agent ? normalizeAgent(agent) : undefined;
  },
  /** 400 with an `id` error for the built-in agent; 409 naming the agents that hand off to it, unless removeFromTeams. */
  deleteAgent: (id: Guid, removeFromTeams = false) =>
    request<void>(removeFromTeams ? `${agentPath(id)}?removeFromTeams=true` : agentPath(id), { method: 'DELETE' }),
};

/** Without the built-in agent. */
export function ownAgents(agents: readonly AgentDto[]): AgentDto[] {
  return agents.filter((agent) => !agent.isBuiltIn);
}

export function useAgents() {
  return useQuery({ queryKey: agentKeys.list, queryFn: ({ signal }) => agentsApi.listAgents(signal), staleTime: 30_000 });
}

export function useAgentOptions() {
  return useQuery({ queryKey: agentKeys.options, queryFn: ({ signal }) => agentsApi.getOptions(signal), staleTime: 60_000 });
}
