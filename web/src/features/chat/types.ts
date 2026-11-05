import type { Guid, IsoDateTime, JsonValue } from '../../lib/types';

export const ToolNames = {
  /** Args: query (required), top_k, source_ids. */
  SearchKnowledgeBase: 'search_knowledge_base',
  /** Args: text (required), match_case, whole_words, output (passages or sources), source_ids, limit, offset. */
  GrepSources: 'grep_sources',
  /** Args: query (words in title, URL or file name), type, status (ready by default, or "any"), limit, offset. */
  FindSources: 'find_sources',
  /** Args: source_id (required), offset, max_chars. */
  ReadSource: 'read_source',
  /** Args: task (required), source_ids. Runs the researcher sub-agent. */
  DelegateResearch: 'delegate_research',
  /** Args: agent (a specialist's key), reason. The specialist answers from here on. */
  TransferToAgent: 'transfer_to_agent',
  /** Args: reason. A specialist hands the conversation back to the front agent. */
  TransferBack: 'transfer_back',
} as const;

export interface ChatRequestDto {
  conversationId: Guid | null;
  message: string;
  /** Restricts retrieval to these sources (new conversations only). */
  sourceIds?: Guid[] | null;
  /** Chat model profile to use instead of the agent's default. */
  profile?: string | null;
  /** Extra Handlebars variables for the system prompt (new conversations only). */
  variables?: Record<string, JsonValue> | null;
  /** Key of the agent to chat with (new conversations only); empty means the built-in agent. */
  agent?: string | null;
  /** Answers a tool call of the latest turn that waits for Confirm or Cancel; the message may then be empty. */
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
} as const;
export type ChatEventType = (typeof ChatEventTypes)[keyof typeof ChatEventTypes];

interface ChatStreamEvent {
  /** "orchestrator" or a sub-agent such as "researcher". */
  agent: string;
  /** For sub-agent events: the orchestrator tool call that started the sub-agent. */
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

/** Shadows the DOM `ErrorEvent` type where it is imported. */
export interface ErrorEvent extends ChatStreamEvent {
  message: string;
  /** e.g. model_not_configured, provider_error, refusal, cancelled, internal_error */
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
}

export type ChatEvent = {
  [K in ChatEventType]: { event: K; data: ChatEventPayloads[K] };
}[ChatEventType];

export interface CitationDto {
  /** Tag used in answers, e.g. "S3" for [S3]. */
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
}

export interface ToolConfirmation {
  status: 'pending' | 'confirmed' | 'declined';
  title: string;
  connection: string | null;
  expiresAt: IsoDateTime;
}

/** api and mcp: the agent's HTTP API and MCP server, used by other software with an API key. */
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
  /** Null for the built-in agent. */
  agentId: Guid | null;
  /** Missing from API versions before labels. */
  pinned?: boolean;
  labels?: ConversationLabelDto[];
  /** The specialist answering now; null while the front agent (agentId) answers. */
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
  /** auto: a rule added it, not a person. */
  source: 'manual' | 'auto';
}

export interface LabelDto {
  id: Guid;
  name: string;
  color: LabelColor;
  description: string | null;
  /** Added to matching conversations on its own. */
  autoApply: boolean;
  conversationCount: number;
}

export interface ConversationPage {
  items: ConversationListItem[];
  /** Null on the last page. */
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
  /** Null for the built-in agent. */
  agentId: Guid | null;
  channel: ConversationChannel;
  status: ConversationStatus;
  /** The timeline, by sequence. A question is stored when its turn starts, the answer when it ends. */
  messages: ConversationMessageDto[];
  /** The specialist answering now; null while the front agent (agentId) answers. */
  activeAgent?: ActiveAgentDto | null;
}

type MessageAuthor = 'user' | 'visitor' | 'assistant' | 'staff' | 'system';

export interface ConversationMessageDto {
  id: Guid;
  /** Increasing within the conversation; the event stream resumes after it. */
  sequence: number;
  author: MessageAuthor;
  authorName: string | null;
  text: string;
  citations: CitationDto[];
  turnId: Guid | null;
  createdAt: IsoDateTime;
  /** A notice visitors never see, such as a hand-off to a specialist whose name is hidden. */
  staffOnly?: boolean;
}

/** An event of `GET /api/conversations/{id}/events`. */
export type ConversationLiveEvent =
  | { type: 'message'; message: ConversationMessageDto }
  /** An event of the turn being written, as the POST /api/chat stream sends it. */
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
