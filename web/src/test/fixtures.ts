import type {
  ChatEvent,
  ChatEventPayloads,
  ChatEventType,
  CitationDto,
  ConversationDto,
  ConversationMessageDto,
  ConversationSummaryDto,
  ConversationTurnDto,
} from '../features/chat/types';
import type { SourceDto } from '../features/sources/types';

type Payload<K extends ChatEventType> = Omit<ChatEventPayloads[K], 'agent' | 'parentCallId'> & {
  agent?: string;
  parentCallId?: string | null;
};

/** Builds a typed chat event with orchestrator defaults for `agent` / `parentCallId`. */
export function ev<K extends ChatEventType>(event: K, data: Payload<K>): ChatEvent {
  return { event, data: { agent: 'orchestrator', parentCallId: null, ...data } } as unknown as ChatEvent;
}

export const CONVERSATION_ID = '0199c1e2-0000-7000-8000-00000000c001';
export const TURN_ID = '0199c1e2-0000-7000-8000-00000000a001';

export const citationS1: CitationDto = {
  ref: 'S1',
  sourceId: 'src-handbook',
  chunkId: 'chunk-1',
  sourceTitle: 'Employee handbook',
  sourceUrl: null,
  page: 4,
  snippet: 'New employees receive 25 days of paid vacation per calendar year.',
  score: 0.83,
};

export const citationS2: CitationDto = {
  ref: 'S2',
  sourceId: 'src-policy',
  chunkId: 'chunk-2',
  sourceTitle: 'Leave policy',
  sourceUrl: 'https://intranet.example.com/leave-policy',
  page: null,
  snippet: 'Vacation requests are approved by the line manager.',
  score: 0.77,
};

export const citationS3: CitationDto = {
  ref: 'S3',
  sourceId: 'src-handbook',
  chunkId: 'chunk-3',
  sourceTitle: 'Employee handbook',
  sourceUrl: null,
  page: 9,
  snippet: 'Unused vacation days can be carried over until March.',
  score: 0.71,
};

/** A complete turn: search, delegated research (nested events), answer with citations, title update. */
export function fullTurnEvents(): ChatEvent[] {
  return [
    ev('conversation', { conversationId: CONVERSATION_ID, title: 'New conversation', isNew: true }),
    ev('agent-started', { provider: 'anthropic', model: 'claude-opus-5-5' }),
    ev('reasoning-delta', { text: 'The user asks about vacation. ' }),
    ev('reasoning-delta', { text: 'I should search the handbook.' }),
    ev('tool-call', { callId: 'call-search', toolName: 'search_knowledge_base', arguments: { query: 'vacation days', top_k: 6 } }),
    ev('tool-result', {
      callId: 'call-search',
      toolName: 'search_knowledge_base',
      isError: false,
      summary: 'Found 2 passages in 2 sources',
      durationMs: 412,
      citations: [citationS1, citationS2],
    }),
    ev('tool-call', { callId: 'call-research', toolName: 'delegate_research', arguments: { task: 'Check carry-over rules' } }),
    ev('agent-started', { agent: 'researcher', parentCallId: 'call-research', provider: 'anthropic', model: 'claude-opus-5-5' }),
    ev('reasoning-delta', { agent: 'researcher', parentCallId: 'call-research', text: 'Looking for carry-over.' }),
    ev('tool-call', {
      agent: 'researcher',
      parentCallId: 'call-research',
      callId: 'call-r1',
      toolName: 'search_knowledge_base',
      arguments: { query: 'carry over vacation' },
    }),
    ev('tool-result', {
      agent: 'researcher',
      parentCallId: 'call-research',
      callId: 'call-r1',
      toolName: 'search_knowledge_base',
      isError: false,
      summary: 'Found 1 passage',
      durationMs: 250,
      citations: [citationS3],
    }),
    ev('text-delta', { agent: 'researcher', parentCallId: 'call-research', text: 'Days carry over until March [S3].' }),
    ev('usage', { agent: 'researcher', parentCallId: 'call-research', provider: 'anthropic', model: 'claude-opus-5-5', inputTokens: 900, outputTokens: 120, totalTokens: 1020 }),
    ev('agent-completed', { agent: 'researcher', parentCallId: 'call-research', finishReason: 'stop', iterations: 2 }),
    ev('tool-result', {
      callId: 'call-research',
      toolName: 'delegate_research',
      isError: false,
      summary: 'Unused days carry over until March.',
      durationMs: 3100,
      citations: [],
    }),
    ev('text-delta', { text: 'New employees get **25 days** [S1]. ' }),
    ev('text-delta', { text: 'Managers approve requests [S2], and days carry over until March [S3].' }),
    ev('usage', { provider: 'anthropic', model: 'claude-opus-5-5', inputTokens: 1500, outputTokens: 200, totalTokens: 1700 }),
    ev('usage', { provider: 'anthropic', model: 'claude-opus-5-5', inputTokens: 500, outputTokens: 100, totalTokens: null }),
    ev('agent-completed', { finishReason: 'stop', iterations: 3 }),
    ev('conversation', { conversationId: CONVERSATION_ID, title: 'Vacation policy', isNew: false }),
    ev('done', { conversationId: CONVERSATION_ID, turnId: TURN_ID, finishReason: 'stop' }),
  ];
}

export function turnDto(overrides: Partial<ConversationTurnDto> = {}): ConversationTurnDto {
  return {
    id: TURN_ID,
    sequence: 1,
    userMessage: 'How many vacation days do I get?',
    assistantMessage: 'You get 25 days [S1].',
    toolCalls: [],
    citations: [citationS1],
    provider: 'anthropic',
    model: 'claude-opus-5-5',
    inputTokens: 1200,
    outputTokens: 80,
    finishReason: 'stop',
    error: null,
    durationMs: 2400,
    createdAt: '2026-10-22T08:00:00Z',
    traceId: null,
    ...overrides,
  };
}

/** A timeline message; questions are by the user. */
export function messageDto(overrides: Partial<ConversationMessageDto> & Pick<ConversationMessageDto, 'sequence'>): ConversationMessageDto {
  return {
    id: `message-${overrides.sequence}`,
    author: 'user',
    authorName: null,
    text: 'How many vacation days do I get?',
    citations: [],
    turnId: TURN_ID,
    createdAt: '2026-10-22T08:00:00Z',
    ...overrides,
  };
}

/** The question and answer messages of a stored turn. */
export function turnMessages(turn: ConversationTurnDto, firstSequence: number): ConversationMessageDto[] {
  return [
    messageDto({ sequence: firstSequence, text: turn.userMessage, turnId: turn.id }),
    messageDto({ sequence: firstSequence + 1, author: 'assistant', text: turn.assistantMessage, citations: turn.citations, turnId: turn.id }),
  ];
}

/** "Vacation policy" with one stored turn and its messages. */
export function conversationDto(overrides: Partial<ConversationDto> = {}): ConversationDto {
  const turns = overrides.turns ?? [turnDto()];
  return {
    id: CONVERSATION_ID,
    title: 'Vacation policy',
    agentName: 'orchestrator',
    promptName: 'orchestrator-system',
    promptVersion: 3,
    promptOrigin: 'langfuse',
    profile: 'orchestrator',
    sourceIds: ['src-handbook'],
    createdAt: '2026-10-22T08:00:00Z',
    updatedAt: '2026-10-22T08:00:10Z',
    turns,
    agentId: null,
    channel: 'portal',
    status: 'ai',
    messages: turns.flatMap((turn, index) => turnMessages(turn, index * 2 + 1)),
    ...overrides,
  };
}

export function conversationSummaryDto(overrides: Partial<ConversationSummaryDto> = {}): ConversationSummaryDto {
  return {
    id: CONVERSATION_ID,
    title: 'Vacation policy',
    turnCount: 1,
    createdAt: '2026-10-22T08:00:00Z',
    updatedAt: '2026-10-22T08:00:10Z',
    channel: 'portal',
    status: 'ai',
    agentId: null,
    ...overrides,
  };
}

export function sourceDto(overrides: Partial<SourceDto> = {}): SourceDto {
  return {
    id: 'src-handbook',
    type: 'pdf',
    status: 'ready',
    title: 'Employee handbook',
    url: null,
    site: null,
    fileName: 'handbook.pdf',
    contentType: 'application/pdf',
    sizeBytes: 482_000,
    chunkCount: 42,
    pageCount: 18,
    tokenEstimate: 21_000,
    embeddingModel: 'openai:text-embedding-3-small:1536',
    error: null,
    attempts: 1,
    stage: null,
    embeddedChunks: 42,
    jobRunId: null,
    jobRunUrl: null,
    createdAt: '2026-10-21T10:00:00Z',
    updatedAt: '2026-10-21T10:01:00Z',
    processedAt: '2026-10-21T10:01:00Z',
    metadata: {},
    folderId: null,
    ...overrides,
  };
}
