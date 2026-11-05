import type { JsonValue } from '../../lib/types';
import {
  ToolNames,
  type AgentCompletedEvent,
  type AgentStartedEvent,
  type ChatEvent,
  type CitationDto,
  type ConversationTurnDto,
  type ErrorEvent,
  type ToolCallDto,
  type ToolConfirmation,
  type ToolCallEvent,
  type ToolResultEvent,
  type UsageEvent,
} from './types';

interface UsageTotals {
  inputTokens: number | null;
  outputTokens: number | null;
  totalTokens: number | null;
}

export interface TurnError {
  message: string;
  code: string | null;
  agent: string;
}

type ToolStatus = 'running' | 'success' | 'error' | 'cancelled';

/** What a sub-agent, e.g. the researcher behind delegate_research, streamed while it ran. */
export interface AgentActivity {
  agent: string;
  provider: string | null;
  model: string | null;
  text: string;
  reasoning: string;
  usage: UsageTotals | null;
  errors: TurnError[];
  status: 'running' | 'completed' | 'cancelled';
  finishReason: string | null;
  iterations: number | null;
  /** A tool call happened since the last delta, so the next one starts a new paragraph. */
  textBreakPending: boolean;
  reasoningBreakPending: boolean;
}

export interface ToolCallView {
  callId: string;
  toolName: string;
  agent: string;
  parentCallId: string | null;
  arguments: JsonValue | null;
  status: ToolStatus;
  summary: string | null;
  durationMs: number | null;
  citations: CitationDto[];
  children: ToolCallView[];
  subAgent: AgentActivity | null;
  confirmation?: ToolConfirmation | null;
  result?: string | null;
}

/** pending: the question is stored, but no answer has reached this view yet. cancelled: stopped before it was complete. */
type TurnStatus = 'pending' | 'streaming' | 'complete' | 'error' | 'cancelled';

export interface TurnView {
  /** The client id for turns streamed in this session, the server id for history. */
  key: string;
  /** The server id once known; the client id while streaming. */
  id: string;
  persisted: boolean;
  userMessage: string;
  text: string;
  /** Not persisted by the API. */
  reasoning: string;
  tools: ToolCallView[];
  /** Every citation produced during the turn, unique by ref. */
  citations: CitationDto[];
  provider: string | null;
  model: string | null;
  usage: UsageTotals | null;
  errors: TurnError[];
  status: TurnStatus;
  finishReason: string | null;
  durationMs: number | null;
  /** Client time in ms; null for history turns. */
  startedAt: number | null;
  /** For turns sent from this tab: the last timeline sequence it knew then. Its question is stored after it. */
  sentAfterSequence: number | null;
  textBreakPending: boolean;
  reasoningBreakPending: boolean;
  traceId?: string | null;
}

export interface ChatState {
  conversationId: string | null;
  title: string | null;
  /** Turns streamed in this tab. */
  turns: TurnView[];
  streaming: boolean;
}

export type ChatAction =
  | { type: 'reset'; conversationId: string | null }
  | { type: 'send'; id: string; message: string; at: number; sentAfterSequence?: number | null }
  /** A turn another tab started, or one that was running before a reload, followed through the conversation's events. */
  | { type: 'follow'; turnId: string; message: string; at: number }
  /** The API refused the message before a turn started, so the question is taken back. */
  | { type: 'refused' }
  | { type: 'event'; event: ChatEvent; at: number }
  /** Without a preceding `done`, the turn is marked as interrupted. */
  | { type: 'stream-ended'; at: number }
  | { type: 'aborted'; at: number }
  | { type: 'failed'; message: string; code?: string | null; at: number };

const ORCHESTRATOR = 'orchestrator';
export const INTERRUPTED_MESSAGE = 'The response ended unexpectedly. Try sending the message again.';

/** "stopped": someone stopped the answer on the server, which keeps the text so far. Older API versions sent "cancelled". */
function isStoppedFinish(finishReason: string | null | undefined): boolean {
  return finishReason === 'stopped' || finishReason === 'cancelled';
}

export function createChatState(conversationId: string | null = null): ChatState {
  return { conversationId, title: null, turns: [], streaming: false };
}

export function createTurn(id: string, userMessage: string, startedAt: number | null): TurnView {
  return {
    key: id,
    id,
    persisted: false,
    userMessage,
    text: '',
    reasoning: '',
    tools: [],
    citations: [],
    provider: null,
    model: null,
    usage: null,
    errors: [],
    status: 'streaming',
    finishReason: null,
    durationMs: null,
    startedAt,
    sentAfterSequence: null,
    textBreakPending: false,
    reasoningBreakPending: false,
  };
}

function createActivity(agent: string): AgentActivity {
  return {
    agent,
    provider: null,
    model: null,
    text: '',
    reasoning: '',
    usage: null,
    errors: [],
    status: 'running',
    finishReason: null,
    iterations: null,
    textBreakPending: false,
    reasoningBreakPending: false,
  };
}

function createTool(callId: string, toolName: string, agent: string, parentCallId: string | null): ToolCallView {
  return {
    callId,
    toolName,
    agent,
    parentCallId,
    arguments: null,
    status: 'running',
    summary: null,
    durationMs: null,
    citations: [],
    children: [],
    subAgent: null,
  };
}

/** Arguments arrive as a JSON object; a JSON-encoded string is tolerated too. */
function normalizeArguments(value: JsonValue | null | undefined): JsonValue | null {
  if (typeof value === 'string') {
    try {
      return JSON.parse(value) as JsonValue;
    } catch {
      return value;
    }
  }
  return value ?? null;
}

function addNullable(a: number | null, b: number | null | undefined): number | null {
  if (b === null || b === undefined) {
    return a;
  }
  return (a ?? 0) + b;
}

function addUsage(current: UsageTotals | null, event: Pick<UsageEvent, 'inputTokens' | 'outputTokens' | 'totalTokens'>): UsageTotals {
  const base = current ?? { inputTokens: null, outputTokens: null, totalTokens: null };
  const inputTokens = addNullable(base.inputTokens, event.inputTokens);
  const outputTokens = addNullable(base.outputTokens, event.outputTokens);
  const eventTotal = event.totalTokens ?? (event.inputTokens != null || event.outputTokens != null
    ? (event.inputTokens ?? 0) + (event.outputTokens ?? 0)
    : null);
  return { inputTokens, outputTokens, totalTokens: addNullable(base.totalTokens, eventTotal) };
}

function appendSegment(existing: string, delta: string, breakPending: boolean): string {
  if (!breakPending || existing.length === 0 || delta.length === 0) {
    return existing + delta;
  }
  if (existing.endsWith('\n\n') || delta.startsWith('\n\n')) {
    return existing + delta;
  }
  return existing + (existing.endsWith('\n') ? '\n' : '\n\n') + delta;
}

function mergeCitations(existing: CitationDto[], incoming: readonly CitationDto[] | null | undefined): CitationDto[] {
  if (!incoming || incoming.length === 0) {
    return existing;
  }
  const byRef = new Map(existing.map((c) => [c.ref, c] as const));
  for (const citation of incoming) {
    if (citation && typeof citation.ref === 'string') {
      byRef.set(citation.ref, citation);
    }
  }
  return [...byRef.values()];
}

function findTool(tools: readonly ToolCallView[], callId: string): ToolCallView | undefined {
  for (const tool of tools) {
    if (tool.callId === callId) {
      return tool;
    }
    const nested = findTool(tool.children, callId);
    if (nested) {
      return nested;
    }
  }
  return undefined;
}

/** Returns the same array when no tool has `callId`. */
function updateTool(
  tools: ToolCallView[],
  callId: string,
  update: (tool: ToolCallView) => ToolCallView,
): ToolCallView[] {
  let changed = false;
  const next = tools.map((tool) => {
    if (changed) {
      return tool;
    }
    if (tool.callId === callId) {
      changed = true;
      return update(tool);
    }
    const children = updateTool(tool.children, callId, update);
    if (children !== tool.children) {
      changed = true;
      return { ...tool, children };
    }
    return tool;
  });
  return changed ? next : tools;
}

function mapToolsDeep(tools: ToolCallView[], fn: (tool: ToolCallView) => ToolCallView): ToolCallView[] {
  return tools.map((tool) => fn({ ...tool, children: mapToolsDeep(tool.children, fn) }));
}

function withParent(turn: TurnView, parentCallId: string, agent: string, update: (parent: ToolCallView) => ToolCallView): TurnView {
  let tools = turn.tools;
  if (!findTool(tools, parentCallId)) {
    // A nested event for an unknown parent should not happen; keep the activity visible anyway.
    tools = [...tools, createTool(parentCallId, ToolNames.DelegateResearch, ORCHESTRATOR, null)];
  }
  return {
    ...turn,
    tools: updateTool(tools, parentCallId, (parent) => update({ ...parent, subAgent: parent.subAgent ?? createActivity(agent) })),
  };
}

function updateActivity(turn: TurnView, parentCallId: string, agent: string, update: (activity: AgentActivity) => AgentActivity): TurnView {
  return withParent(turn, parentCallId, agent, (parent) => ({ ...parent, subAgent: update(parent.subAgent!) }));
}

function applyAgentStarted(turn: TurnView, data: AgentStartedEvent): TurnView {
  if (data.parentCallId) {
    return updateActivity(turn, data.parentCallId, data.agent, (a) => ({ ...a, provider: data.provider, model: data.model, status: 'running' }));
  }
  return { ...turn, provider: data.provider, model: data.model };
}

function applyTextDelta(turn: TurnView, agent: string, parentCallId: string | null, text: string): TurnView {
  if (!text) {
    return turn;
  }
  if (parentCallId) {
    return updateActivity(turn, parentCallId, agent, (a) => ({
      ...a,
      text: appendSegment(a.text, text, a.textBreakPending),
      textBreakPending: false,
    }));
  }
  return { ...turn, text: appendSegment(turn.text, text, turn.textBreakPending), textBreakPending: false };
}

function applyReasoningDelta(turn: TurnView, agent: string, parentCallId: string | null, text: string): TurnView {
  if (!text) {
    return turn;
  }
  if (parentCallId) {
    return updateActivity(turn, parentCallId, agent, (a) => ({
      ...a,
      reasoning: appendSegment(a.reasoning, text, a.reasoningBreakPending),
      reasoningBreakPending: false,
    }));
  }
  return {
    ...turn,
    reasoning: appendSegment(turn.reasoning, text, turn.reasoningBreakPending),
    reasoningBreakPending: false,
  };
}

function applyToolCall(turn: TurnView, data: ToolCallEvent): TurnView {
  const args = normalizeArguments(data.arguments);
  if (findTool(turn.tools, data.callId)) {
    return { ...turn, tools: updateTool(turn.tools, data.callId, (tool) => ({ ...tool, arguments: args })) };
  }
  const tool = { ...createTool(data.callId, data.toolName, data.agent, data.parentCallId), arguments: args };
  if (data.parentCallId) {
    return withParent(turn, data.parentCallId, data.agent, (parent) => ({
      ...parent,
      children: [...parent.children, tool],
      subAgent: { ...parent.subAgent!, textBreakPending: true, reasoningBreakPending: true },
    }));
  }
  return { ...turn, tools: [...turn.tools, tool], textBreakPending: true, reasoningBreakPending: true };
}

function applyToolResult(turn: TurnView, data: ToolResultEvent): TurnView {
  const finish = (tool: ToolCallView): ToolCallView => ({
    ...tool,
    toolName: tool.toolName || data.toolName,
    status: data.isError ? 'error' : 'success',
    summary: data.summary ?? null,
    durationMs: data.durationMs ?? null,
    citations: mergeCitations(tool.citations, data.citations),
    subAgent: tool.subAgent && tool.subAgent.status === 'running' ? { ...tool.subAgent, status: 'completed' } : tool.subAgent,
  });

  let next = turn;
  if (!findTool(turn.tools, data.callId)) {
    // A result without a preceding call: synthesize the call so the result is not lost.
    next = applyToolCall(turn, { ...data, arguments: null });
  }
  return {
    ...next,
    tools: updateTool(next.tools, data.callId, finish),
    citations: mergeCitations(next.citations, data.citations),
  };
}

function applyUsage(turn: TurnView, data: UsageEvent): TurnView {
  if (data.parentCallId) {
    return updateActivity(turn, data.parentCallId, data.agent, (a) => ({
      ...a,
      usage: addUsage(a.usage, data),
      provider: a.provider ?? data.provider ?? null,
      model: a.model ?? data.model ?? null,
    }));
  }
  return {
    ...turn,
    usage: addUsage(turn.usage, data),
    provider: turn.provider ?? data.provider ?? null,
    model: turn.model ?? data.model ?? null,
  };
}

function applyAgentCompleted(turn: TurnView, data: AgentCompletedEvent): TurnView {
  if (data.parentCallId) {
    return updateActivity(turn, data.parentCallId, data.agent, (a) => ({
      ...a,
      status: 'completed',
      finishReason: data.finishReason ?? null,
      iterations: data.iterations ?? null,
    }));
  }
  return { ...turn, finishReason: data.finishReason ?? turn.finishReason };
}

function applyError(turn: TurnView, data: ErrorEvent): TurnView {
  const error: TurnError = { message: data.message || 'Unknown error', code: data.code ?? null, agent: data.agent };
  if (data.parentCallId) {
    return updateActivity(turn, data.parentCallId, data.agent, (a) => ({ ...a, errors: [...a.errors, error] }));
  }
  return { ...turn, errors: [...turn.errors, error] };
}

function settleTools(tools: ToolCallView[]): ToolCallView[] {
  return mapToolsDeep(tools, (tool) => ({
    ...tool,
    status: tool.status === 'running' ? 'cancelled' : tool.status,
    subAgent: tool.subAgent && tool.subAgent.status === 'running' ? { ...tool.subAgent, status: 'cancelled' } : tool.subAgent,
  }));
}

function hasRunning(tools: readonly ToolCallView[]): boolean {
  return tools.some((t) => t.status === 'running' || t.subAgent?.status === 'running' || hasRunning(t.children));
}

function finalize(turn: TurnView, status: TurnStatus, at: number, extra: Partial<TurnView> = {}): TurnView {
  return {
    ...turn,
    ...extra,
    status,
    tools: hasRunning(turn.tools) ? settleTools(turn.tools) : turn.tools,
    durationMs: extra.durationMs ?? turn.durationMs ?? (turn.startedAt !== null ? Math.max(0, at - turn.startedAt) : null),
    textBreakPending: false,
    reasoningBreakPending: false,
  };
}

function applyEventToTurn(turn: TurnView, event: ChatEvent, at: number): TurnView {
  switch (event.event) {
    case 'agent-started':
      return applyAgentStarted(turn, event.data);
    case 'text-delta':
      return applyTextDelta(turn, event.data.agent, event.data.parentCallId, event.data.text);
    case 'reasoning-delta':
      return applyReasoningDelta(turn, event.data.agent, event.data.parentCallId, event.data.text);
    case 'tool-call':
      return applyToolCall(turn, event.data);
    case 'tool-result':
      return applyToolResult(turn, event.data);
    case 'usage':
      return applyUsage(turn, event.data);
    case 'agent-completed':
      return applyAgentCompleted(turn, event.data);
    case 'error':
      return applyError(turn, event.data);
    case 'done': {
      const hasErrors = turn.errors.some((e) => e.code !== 'cancelled');
      const stopped = isStoppedFinish(event.data.finishReason) || turn.errors.some((e) => e.code === 'cancelled');
      return finalize(turn, hasErrors ? 'error' : stopped ? 'cancelled' : 'complete', at, {
        id: event.data.turnId || turn.id,
        persisted: Boolean(event.data.turnId),
        finishReason: event.data.finishReason ?? turn.finishReason,
      });
    }
    case 'confirmation-required': {
      const data = event.data;
      const confirmation: ToolConfirmation = { status: 'pending', title: data.title, connection: data.connection ?? null, expiresAt: data.expiresAt };
      return { ...turn, tools: updateTool(turn.tools, data.callId, (tool) => ({ ...tool, confirmation })) };
    }
    case 'conversation':
      return turn;
  }
}

export function pendingConfirmations(turns: readonly TurnView[], now: number = Date.now()): ToolCallView[] {
  const last = turns.at(-1);
  if (!last || last.status === 'streaming') {
    return [];
  }
  return last.tools.filter((tool) => tool.confirmation?.status === 'pending' && Date.parse(tool.confirmation.expiresAt) > now);
}

function updateLastStreamingTurn(state: ChatState, update: (turn: TurnView) => TurnView): ChatState {
  const index = state.turns.length - 1;
  const last = state.turns[index];
  if (!last || last.status !== 'streaming') {
    return state;
  }
  const turns = state.turns.slice();
  turns[index] = update(last);
  return { ...state, turns };
}

export function chatReducer(state: ChatState, action: ChatAction): ChatState {
  switch (action.type) {
    case 'reset':
      return createChatState(action.conversationId);

    case 'send': {
      // A previous turn that is somehow still open is closed first.
      const settled = updateLastStreamingTurn(state, (turn) => finalize(turn, 'cancelled', action.at));
      const turn = { ...createTurn(action.id, action.message, action.at), sentAfterSequence: action.sentAfterSequence ?? null };
      return { ...settled, turns: [...settled.turns, turn], streaming: true };
    }

    case 'follow': {
      // Keyed by the server id, so the row keeps its key when the stored copy of the turn replaces it.
      const settled = updateLastStreamingTurn(state, (turn) => finalize(turn, 'cancelled', action.at));
      return { ...settled, turns: [...settled.turns, createTurn(action.turnId, action.message, null)], streaming: true };
    }

    case 'refused': {
      const last = state.turns.at(-1);
      return { ...state, turns: last?.status === 'streaming' ? state.turns.slice(0, -1) : state.turns, streaming: false };
    }

    case 'event': {
      const { event } = action;
      let next = state;
      if (event.event === 'conversation') {
        next = { ...state, conversationId: event.data.conversationId, title: event.data.title || state.title };
      } else if (event.event === 'done' && event.data.conversationId) {
        next = { ...state, conversationId: event.data.conversationId };
      }
      next = updateLastStreamingTurn(next, (turn) => applyEventToTurn(turn, event, action.at));
      if (event.event === 'done') {
        next = { ...next, streaming: false };
      }
      return next;
    }

    case 'stream-ended': {
      const next = updateLastStreamingTurn(state, (turn) =>
        finalize(turn, 'error', action.at, {
          errors: [...turn.errors, { message: INTERRUPTED_MESSAGE, code: 'interrupted', agent: ORCHESTRATOR }],
        }),
      );
      return { ...next, streaming: false };
    }

    case 'aborted': {
      const next = updateLastStreamingTurn(state, (turn) => finalize(turn, 'cancelled', action.at));
      return { ...next, streaming: false };
    }

    case 'failed': {
      const next = updateLastStreamingTurn(state, (turn) =>
        finalize(turn, 'error', action.at, {
          errors: [...turn.errors, { message: action.message, code: action.code ?? null, agent: ORCHESTRATOR }],
        }),
      );
      return { ...next, streaming: false };
    }
  }
}

/** Sub-agent calls in the flat persisted list reference their parent call. */
export function buildToolTree(calls: readonly ToolCallDto[]): ToolCallView[] {
  const views = new Map<string, ToolCallView>();
  for (const call of calls) {
    views.set(call.callId, {
      ...createTool(call.callId, call.toolName, call.agent || ORCHESTRATOR, call.parentCallId ?? null),
      arguments: normalizeArguments(call.arguments),
      status: call.isError ? 'error' : 'success',
      summary: call.summary ?? null,
      durationMs: call.durationMs ?? null,
      confirmation: call.confirmation ?? null,
      result: call.result ?? null,
    });
  }
  const roots: ToolCallView[] = [];
  for (const call of calls) {
    const view = views.get(call.callId)!;
    const parent = call.parentCallId ? views.get(call.parentCallId) : undefined;
    if (parent && parent !== view) {
      parent.children.push(view);
      parent.subAgent ??= { ...createActivity(view.agent), status: 'completed' };
    } else {
      roots.push(view);
    }
  }
  return roots;
}

export function turnFromDto(dto: ConversationTurnDto): TurnView {
  const errors: TurnError[] = dto.error ? [{ message: dto.error, code: null, agent: ORCHESTRATOR }] : [];
  const hasUsage = dto.inputTokens != null || dto.outputTokens != null;
  return {
    ...createTurn(dto.id, dto.userMessage ?? '', null),
    persisted: true,
    text: dto.assistantMessage ?? '',
    tools: buildToolTree(dto.toolCalls ?? []),
    citations: mergeCitations([], dto.citations ?? []),
    provider: dto.provider ?? null,
    model: dto.model ?? null,
    usage: hasUsage
      ? {
          inputTokens: dto.inputTokens ?? null,
          outputTokens: dto.outputTokens ?? null,
          totalTokens: (dto.inputTokens ?? 0) + (dto.outputTokens ?? 0),
        }
      : null,
    errors,
    status: errors.length > 0 ? 'error' : isStoppedFinish(dto.finishReason) ? 'cancelled' : 'complete',
    finishReason: dto.finishReason ?? null,
    durationMs: dto.durationMs ?? null,
    traceId: dto.traceId ?? null,
  };
}

/** Stored turns, each replaced by its live copy from this tab (which has the reasoning), then live turns not stored yet. */
export function mergeTurns(history: readonly TurnView[], live: readonly TurnView[]): TurnView[] {
  if (live.length === 0) {
    return history.slice();
  }
  const liveById = new Map(live.filter((t) => t.persisted).map((t) => [t.id, t] as const));
  const historyIds = new Set(history.map((t) => t.id));
  const merged = history.map((turn) => liveById.get(turn.id) ?? turn);
  for (const turn of live) {
    if (!historyIds.has(turn.id)) {
      merged.push(turn);
    }
  }
  return merged;
}

/** Citation tags are unique per conversation. */
export function citationIndex(turns: readonly TurnView[]): Map<string, CitationDto> {
  const index = new Map<string, CitationDto>();
  for (const turn of turns) {
    for (const citation of turn.citations) {
      index.set(citation.ref, citation);
    }
  }
  return index;
}
