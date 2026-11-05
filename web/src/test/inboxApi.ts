import type { ConversationSearchItem, HandoverDto, InboxConversationDetail, InboxConversationDto } from '../features/inbox/types';
import { agentGuid } from './agentsApi';
import { citationS1, messageDto, turnDto } from './fixtures';
import { widgetGuid } from './widgetsApi';

export const WIDGET_CONVERSATION_ID = '0199c1e2-0000-7000-8000-00000000d001';
const WIDGET_TURN_ID = '0199c1e2-0000-7000-8000-00000000e001';
const VISITOR_ID = '0199c1e2-0000-7000-8000-00000000f001';

/** A row of GET /api/conversations?channel=widget. */
export function inboxConversation(overrides: Partial<InboxConversationDto> = {}): InboxConversationDto {
  return {
    id: WIDGET_CONVERSATION_ID,
    title: 'Return an order',
    turnCount: 1,
    createdAt: '2026-10-31T09:00:00Z',
    updatedAt: '2026-10-31T09:05:00Z',
    channel: 'widget',
    status: 'handover_pending',
    agentId: agentGuid(2),
    widgetId: widgetGuid(1),
    preview: "I've asked a colleague to join. They'll reply here as soon as they can.",
    handoverStatus: 'pending',
    pinned: false,
    labels: [],
    ...overrides,
  };
}

/** A visitor's question, the answer with a tool call, and the system message of a request for a person. */
export function widgetConversation(overrides: Partial<InboxConversationDetail> = {}): InboxConversationDetail {
  const turn = turnDto({
    id: WIDGET_TURN_ID,
    userMessage: 'Can I return shoes I wore once?',
    assistantMessage: 'Worn shoes cannot be returned [S1].',
    citations: [citationS1],
    toolCalls: [
      {
        callId: 'call-1',
        toolName: 'search_knowledge_base',
        agent: 'orchestrator',
        parentCallId: null,
        arguments: { query: 'return worn shoes' },
        summary: 'Found 1 passage',
        isError: false,
        durationMs: 300,
      },
    ],
    createdAt: '2026-10-31T09:00:00Z',
  });
  return {
    id: WIDGET_CONVERSATION_ID,
    title: 'Return an order',
    agentName: 'Website helper',
    promptName: 'orchestrator-system',
    promptVersion: 3,
    promptOrigin: 'langfuse',
    profile: 'orchestrator',
    sourceIds: [],
    createdAt: '2026-10-31T09:00:00Z',
    updatedAt: '2026-10-31T09:05:00Z',
    turns: [turn],
    agentId: agentGuid(2),
    channel: 'widget',
    status: 'handover_pending',
    widgetId: widgetGuid(1),
    visitorId: VISITOR_ID,
    pinned: false,
    contact: null,
    createdBy: null,
    assignee: null,
    messages: [
      messageDto({ sequence: 1, author: 'visitor', authorName: 'Jane', text: turn.userMessage, turnId: turn.id, createdAt: '2026-10-31T09:00:00Z' }),
      messageDto({ sequence: 2, author: 'assistant', text: turn.assistantMessage, citations: [citationS1], turnId: turn.id, createdAt: '2026-10-31T09:00:05Z' }),
      messageDto({
        sequence: 3,
        author: 'system',
        text: "I've asked a colleague to join. They'll reply here as soon as they can.",
        turnId: null,
        createdAt: '2026-10-31T09:05:00Z',
      }),
    ],
    ...overrides,
  };
}

export function handoverDto(overrides: Partial<HandoverDto> = {}): HandoverDto {
  return {
    id: '0199c1e2-0000-7000-8000-0000000a1001',
    conversationId: WIDGET_CONVERSATION_ID,
    channel: 'email',
    status: 'pending',
    source: 'visitor',
    reason: 'Wants to return worn shoes',
    summary: 'The visitor bought shoes last week and wore them once.',
    assignee: null,
    requestedAt: '2026-10-31T09:05:00Z',
    acceptedAt: null,
    resolvedAt: null,
    lastError: null,
    ...overrides,
  };
}

/** A row of GET /api/conversations/search: the conversation of widgetConversation, waiting for a person. */
export function searchItem(overrides: Partial<ConversationSearchItem> = {}): ConversationSearchItem {
  return {
    id: WIDGET_CONVERSATION_ID,
    title: 'Return an order',
    channel: 'widget',
    status: 'handover_pending',
    agentId: agentGuid(2),
    widgetId: widgetGuid(1),
    createdAt: '2026-10-31T09:00:00Z',
    updatedAt: '2026-10-31T09:05:00Z',
    messageCount: 3,
    preview: "I've asked a colleague to join.",
    match: null,
    handoverStatus: 'pending',
    rating: null,
    thumbsDown: 0,
    pinned: false,
    labels: [],
    contact: null,
    assignee: null,
    unread: false,
    ...overrides,
  };
}

/** GET /api/conversations/counts as the server sends it. */
export function countsDto(
  overrides: Partial<{
    handoverPending: number;
    human: number;
    ai: number;
    closed: number;
    pinned: number;
    all: number;
    mine: number;
    unassigned: number;
    unread: Partial<Record<'handoverPending' | 'human' | 'ai' | 'closed' | 'pinned' | 'all' | 'mine' | 'unassigned', number>>;
    labels: Array<{ labelId: string; count: number }>;
  }> = {},
) {
  return { handoverPending: 1, human: 0, ai: 1, closed: 0, pinned: 0, all: 2, mine: 0, unassigned: 1, unread: {}, labels: [], ...overrides };
}
