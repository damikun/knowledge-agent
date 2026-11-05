import type { AgentDto, AgentOptionsDto } from '../features/agents/types';
import { jsonResponse, type FetchHandler } from './utils';

const AGENT_TOOLS = ['search_knowledge_base', 'grep_sources', 'find_sources', 'read_source', 'delegate_research'];

/** A GUID-shaped agent id (`n` = 1 → "0199c1e2-0000-7000-8000-0000000a0001"). */
export function agentGuid(n: number): string {
  return `0199c1e2-0000-7000-8000-0000000a${String(n).padStart(4, '0')}`;
}

export function agentDto(overrides: Partial<AgentDto> = {}): AgentDto {
  return {
    id: agentGuid(2),
    key: 'website-helper',
    name: 'Website helper',
    description: 'Answers visitors of the public website.',
    instructions: 'Be friendly.',
    language: null,
    profileName: 'orchestrator',
    folderIds: [],
    tools: [...AGENT_TOOLS],
    allowGeneralKnowledge: false,
    handoverPolicy: 'onRequest',
    isBuiltIn: false,
    createdAt: '2026-10-30T08:00:00Z',
    updatedAt: '2026-10-30T08:00:00Z',
    ...overrides,
  };
}

/** The agent of the portal chat, as the API seeds it. */
export const builtInAgent = agentDto({
  id: agentGuid(1),
  key: 'internal',
  name: 'Knowledge Agent',
  description: "The agent of the portal's chat, for admins and support staff.",
  instructions: null,
  allowGeneralKnowledge: true,
  handoverPolicy: 'never',
  isBuiltIn: true,
});

export function agentOptions(overrides: Partial<AgentOptionsDto> = {}): AgentOptionsDto {
  return { tools: [...AGENT_TOOLS], profiles: ['orchestrator', 'researcher', 'utility'], ...overrides };
}

/** mockFetch routes for GET /api/agents and /options; the getter is read per request, so tests can change the data. */
export function agentsRoutes(getAgents: () => readonly AgentDto[], options: AgentOptionsDto = agentOptions()): Record<string, FetchHandler> {
  return {
    'GET /api/agents': () => jsonResponse(getAgents()),
    'GET /api/agents/options': () => jsonResponse(options),
  };
}
