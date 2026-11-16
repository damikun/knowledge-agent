import type { AgentDto, AgentOptionsDto } from '../features/agents/types';
import { jsonResponse, type FetchHandler } from './utils';

const AGENT_TOOLS = ['search_knowledge_base', 'grep_sources', 'find_sources', 'read_source', 'delegate_research'];

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
    createdAt: '2026-10-30T08:00:00Z',
    updatedAt: '2026-10-30T08:00:00Z',
    ...overrides,
  };
}

export const assistantAgent = agentDto({
  id: agentGuid(1),
  key: 'assistant',
  name: 'Assistant',
  description: 'Answers from all your sources. Change it or add agents of your own.',
  instructions: null,
  allowGeneralKnowledge: true,
  handoverPolicy: 'never',
  createdAt: '2026-10-29T08:00:00Z',
  updatedAt: '2026-10-29T08:00:00Z',
});

export function agentOptions(overrides: Partial<AgentOptionsDto> = {}): AgentOptionsDto {
  return { tools: [...AGENT_TOOLS], profiles: ['orchestrator', 'researcher', 'utility'], ...overrides };
}

export function agentsRoutes(getAgents: () => readonly AgentDto[], options: AgentOptionsDto = agentOptions()): Record<string, FetchHandler> {
  return {
    'GET /api/agents': () => jsonResponse(getAgents()),
    'GET /api/agents/options': () => jsonResponse(options),
  };
}
