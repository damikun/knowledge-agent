import type { Guid, IsoDateTime, JsonValue } from '../../lib/types';

export const ToolNames = {
  SearchKnowledgeBase: 'search_knowledge_base',
  GrepSources: 'grep_sources',
  FindSources: 'find_sources',
  ReadSource: 'read_source',
  DelegateResearch: 'delegate_research',
  TransferToAgent: 'transfer_to_agent',
  TransferBack: 'transfer_back',
} as const;

export interface ChatRequestDto {
  conversationId: Guid | null;
  message: string;
  sourceIds?: Guid[] | null;
  profile?: string | null;
  variables?: Record<string, JsonValue> | null;
  agent?: string | null;
  confirmation?: { callId: string; approved: boolean } | null;
}

export const ChatEventTypes = {
  Conversation: 'conversation',
  AgentStarted: 'agent-started',
  TextDelta: 'text-delta',
  ReasoningDelta: 'reasoning-delta',
  ToolCall: 'tool-call',
  ToolResult: 'tool-result',
  Usage: 'usage',
  AgentCompleted: 'agent-completed',
  Error: 'error',
  Done: 'done',
  ConfirmationRequired: 'confirmation-required',
  StepFinished: 'step-finished',
} as const;
export type ChatEventType = (typeof ChatEventTypes)[keyof typeof ChatEventTypes];

interface ChatStreamEvent {
  agent: string;
  parentCallId: string | null;
}

export interface ConversationEvent extends ChatStreamEvent {
  conversationId: Guid;
  title: string;
  isNew: boolean;
}

export interface AgentStartedEvent extends ChatStreamEvent {
  provider: string;
  model: string;
}

interface TextDeltaEvent extends ChatStreamEvent {
  text: string;
  step?: number;
}

export interface StepFinishedEvent extends ChatStreamEvent {
  step: number;
  kind: 'interim' | 'final';
}

interface ReasoningDeltaEvent extends ChatStreamEvent {
  text: string;
}

export interface ToolCallEvent extends ChatStreamEvent {
  callId: string;
  toolName: string;
  arguments: JsonValue | null;
}

export interface ToolResultEvent extends ChatStreamEvent {
  callId: string;
  toolName: string;
  isError: boolean;
  summary: string;
  durationMs: number;
  citations: CitationDto[];
}

export interface UsageEvent extends ChatStreamEvent {
  provider: string;
  model: string;
  inputTokens: number | null;
  outputTokens: number | null;
  totalTokens: number | null;
}

export interface AgentCompletedEvent extends ChatStreamEvent {
  finishReason: string;
  iterations: number;
}

export interface ErrorEvent extends ChatStreamEvent {
  message: string;
  code: string | null;
}

interface DoneEvent extends ChatStreamEvent {
  conversationId: Guid;
  turnId: Guid;
  finishReason: string;
}

interface ConfirmationRequiredEvent extends ChatStreamEvent {
  callId: string;
  toolName: string;
  title: string;
  connection: string | null;
  arguments: JsonValue | null;
  expiresAt: IsoDateTime;
  details?: ConfirmationDetail[];
}

export interface ConfirmationDetail {
  label: string;
  value: string;
}

export interface ChatEventPayloads {
  conversation: ConversationEvent;
  'agent-started': AgentStartedEvent;
  'text-delta': TextDeltaEvent;
  'reasoning-delta': ReasoningDeltaEvent;
  'tool-call': ToolCallEvent;
  'tool-result': ToolResultEvent;
  usage: UsageEvent;
  'agent-completed': AgentCompletedEvent;
  error: ErrorEvent;
  done: DoneEvent;
  'confirmation-required': ConfirmationRequiredEvent;
  'step-finished': StepFinishedEvent;
}

export type ChatEvent = {
  [K in ChatEventType]: { event: K; data: ChatEventPayloads[K] };
}[ChatEventType];

export interface CitationDto {
  ref: string;
  sourceId: Guid;
  chunkId: Guid;
  sourceTitle: string;
  sourceUrl: string | null;
  page: number | null;
  snippet: string;
  score: number;
}

export interface ToolCallDto {
  callId: string;
  toolName: string;
  agent: string;
  parentCallId: string | null;
  arguments: JsonValue | null;
  summary: string;
  isError: boolean;
  durationMs: number;
  result?: string | null;
  confirmation?: ToolConfirmation | null;
  preamble?: string | null;
}

export interface ToolConfirmation {
  status: 'pending' | 'confirmed' | 'declined';
  title: string;
  connection: string | null;
  expiresAt: IsoDateTime;
  details?: ConfirmationDetail[] | null;
}

export type ConversationChannel = 'portal' | 'widget' | 'api' | 'mcp';

export type ConversationStatus = 'ai' | 'handover_pending' | 'human' | 'closed';

export interface ConversationSummaryDto {
  id: Guid;
  title: string;
  turnCount: number;
  createdAt: IsoDateTime;
  updatedAt: IsoDateTime;
  channel: ConversationChannel;
  status: ConversationStatus;
  agentId: Guid | null;
  pinned?: boolean;
  labels?: ConversationLabelDto[];
  activeAgent?: ActiveAgentDto | null;
}

interface ActiveAgentDto {
  id: Guid;
  name: string;
}

export interface ConversationListItem extends ConversationSummaryDto {
  pinned: boolean;
  labels: ConversationLabelDto[];
}

export type LabelColor = 'gray' | 'blue' | 'green' | 'amber' | 'red' | 'purple' | 'teal' | 'pink';

export interface ConversationLabelDto {
  id: Guid;
  name: string;
  color: LabelColor;
  source: 'manual' | 'auto';
}

export interface LabelDto {
  id: Guid;
  name: string;
  color: LabelColor;
  description: string | null;
  autoApply: boolean;
  conversationCount: number;
}

export interface ConversationPage {
  items: ConversationListItem[];
  nextCursor: string | null;
}

export interface ConversationDto {
  id: Guid;
  title: string;
  agentName: string;
  promptName: string | null;
  promptVersion: number | null;
  promptOrigin: string | null;
  profile: string | null;
  sourceIds: Guid[];
  createdAt: IsoDateTime;
  updatedAt: IsoDateTime;
  turns: ConversationTurnDto[];
  agentId: Guid | null;
  channel: ConversationChannel;
  status: ConversationStatus;
  messages: ConversationMessageDto[];
  activeAgent?: ActiveAgentDto | null;
}

type MessageAuthor = 'user' | 'visitor' | 'assistant' | 'staff' | 'system';

export interface ConversationMessageDto {
  id: Guid;
  sequence: number;
  author: MessageAuthor;
  authorName: string | null;
  text: string;
  citations: CitationDto[];
  turnId: Guid | null;
  createdAt: IsoDateTime;
  staffOnly?: boolean;
}

export type ConversationLiveEvent =
  | { type: 'message'; message: ConversationMessageDto }
  | { type: 'turn'; event: ChatEvent }
  | { type: 'status'; conversationId: Guid; status: ConversationStatus };

export interface ConversationTurnDto {
  id: Guid;
  sequence: number;
  userMessage: string;
  assistantMessage: string;
  toolCalls: ToolCallDto[];
  citations: CitationDto[];
  provider: string | null;
  model: string | null;
  inputTokens: number | null;
  outputTokens: number | null;
  finishReason: string | null;
  error: string | null;
  durationMs: number;
  createdAt: IsoDateTime;
  traceId: string | null;
}
