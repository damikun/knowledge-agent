import { plainText } from './citations';
import type { FormResult, Notice } from './errors';
import type { ReadonlyStore } from './store';
import type { Citation, ConfirmationDetail, ConversationStatus, ConversationSummary, TurnEvent, VisitorMessage } from './types';

type Activity = 'thinking' | 'searching';

export interface StreamingAnswer {
  text: string;
  status: string | null;
  citations: Citation[];
  activity: Activity | null;
  error: string | null;
  done: boolean;
}

export interface PendingTurn {
  id: number;
  question: string;
  questionSequence: number | null;
  answer: StreamingAnswer | null;
}

export type ChatActivity = { kind: 'message'; message: VisitorMessage } | { kind: 'error'; text: string } | { kind: 'status'; text: string };

export interface ChatState {
  phase: 'starting' | 'ready' | 'failed';
  visitorId: string | null;
  conversationId: string | null;
  status: ConversationStatus;
  messages: VisitorMessage[];
  conversationRating: number | null;
  pending: PendingTurn | null;
  followed: StreamingAnswer | null;
  notice: Notice | null;
  offline: boolean;
  confirmations: Array<{ callId: string; title: string; details: ConfirmationDetail[] }>;
  handoverOffered: boolean;
  conversations: ConversationSummary[] | null;
  activity: ChatActivity | null;
}

export interface ConfirmationAnswer {
  callId: string;
  approved: boolean;
}

export interface ContactInput {
  name: string;
  email: string;
}

export type SendResult = { status: 'sent' } | { status: 'failed' } | { status: 'needs-contact'; error: string };

export interface ChatActions {
  retry(): Promise<void>;
  reconnect(): void;
  send(text: string, confirmation?: ConfirmationAnswer): Promise<SendResult>;
  newChat(): Promise<void>;
  loadConversations(): void;
  showConversation(conversationId: string): Promise<void>;
  endChat(): Promise<boolean>;
  saveContact(contact: ContactInput): Promise<FormResult>;
  requestHandover(contact?: ContactInput): Promise<FormResult>;
  cancelHandover(): Promise<boolean>;
  rateAnswer(messageId: string, rating: 1 | -1, comment?: string): Promise<boolean>;
  rateConversation(rating: number): Promise<boolean>;
  dismissNotice(): void;
}

export type Chat = ChatState & ChatActions;

export type ChatStore = ReadonlyStore<Chat>;

export const NO_CONVERSATION = {
  conversationId: null,
  status: 'ai',
  messages: [],
  conversationRating: null,
  followed: null,
  offline: false,
  confirmations: [],
  handoverOffered: false,
} satisfies Partial<ChatState>;

export const INITIAL_CHAT: ChatState = {
  ...NO_CONVERSATION,
  phase: 'starting',
  visitorId: null,
  pending: null,
  notice: null,
  conversations: null,
  activity: null,
};

export function onActivity(chat: ChatStore, listener: (activity: ChatActivity) => void): () => void {
  return chat.subscribe((state, previous) => {
    if (state.activity && state.activity !== previous.activity) {
      listener(state.activity);
    }
  });
}

export function lastSequence(state: ChatState): number {
  return state.messages.at(-1)?.sequence ?? 0;
}

export function mergeMessages(
  state: ChatState,
  incoming: readonly VisitorMessage[],
): Pick<ChatState, 'messages' | 'pending'> & { added: VisitorMessage[] } {
  const before = lastSequence(state);
  const bySequence = new Map(state.messages.map((message) => [message.sequence, message] as const));
  for (const message of incoming) {
    const existing = bySequence.get(message.sequence);
    bySequence.set(message.sequence, existing ? { ...message, rating: message.rating ?? existing.rating } : message);
  }
  const added = incoming.filter((message) => message.sequence > before);
  let pending = state.pending;
  if (pending && pending.questionSequence === null) {
    const question = pending.question;
    const stored = added.find((message) => message.author === 'visitor' && message.text.trim() === question);
    if (stored) {
      pending = { ...pending, questionSequence: stored.sequence };
    }
  }
  return { messages: [...bySequence.values()].sort((a, b) => a.sequence - b.sequence), pending, added };
}

const RETRIEVAL_TOOLS = new Set(['search_knowledge_base', 'grep_sources', 'find_sources', 'read_source', 'delegate_research']);

export function isAnswering(state: ChatState): boolean {
  return state.pending !== null && !state.pending.answer?.done;
}

export function isBusy(state: ChatState): boolean {
  return isAnswering(state) || state.followed !== null;
}

export function emptyAnswer(): StreamingAnswer {
  return { text: '', status: null, citations: [], activity: 'thinking', error: null, done: false };
}

const STATUS_LENGTH = 160;

export function statusLine(text: string): string | null {
  const plain = plainText(text);
  if (plain === '') {
    return null;
  }
  const sentences = plain.match(/[^.!?…]+[.!?…]*/g) ?? [plain];
  const last =
    sentences
      .map((sentence) => sentence.trim())
      .filter((sentence) => sentence !== '')
      .pop() ?? plain;
  return last.length > STATUS_LENGTH ? `${last.slice(0, STATUS_LENGTH - 1).trimEnd()}…` : last;
}

function mergeCitations(existing: Citation[], incoming: Citation[]): Citation[] {
  const known = new Set(existing.map((citation) => citation.ref));
  return [...existing, ...incoming.filter((citation) => !known.has(citation.ref))];
}

export function reduceAnswer(answer: StreamingAnswer, event: TurnEvent): StreamingAnswer {
  switch (event.type) {
    case 'tool-call':
      return { ...answer, activity: RETRIEVAL_TOOLS.has(event.toolName) ? 'searching' : 'thinking' };
    case 'tool-result':
      return { ...answer, citations: mergeCitations(answer.citations, event.citations), activity: answer.text ? null : 'thinking' };
    case 'text-delta':
      return { ...answer, text: answer.text + event.text, status: null, activity: null };
    case 'step-finished':
      return event.kind === 'interim' ? { ...answer, text: '', status: statusLine(answer.text) ?? answer.status, activity: 'thinking' } : answer;
    case 'error':
      return { ...answer, error: event.message || null, status: null, activity: null };
    case 'done':
      return { ...answer, done: true, status: null, activity: null };
    default:
      return answer;
  }
}
