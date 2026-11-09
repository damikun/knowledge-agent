import { isAbortError } from '../../lib/sse';
import { plainText } from './citations';
import { CONVERSATION_BUSY, CONVERSATION_CLOSED, DAILY_CAP_REACHED, isApiError, PERSON_JOINED, WidgetApiError, type WidgetApi } from './api';
import type { HostAttributeValue, HostIdentity } from './host';
import {
  normalizeStatus,
  type AttributeRejection,
  type Citation,
  type ConfirmationDetail,
  type ConversationStatus,
  type LiveEvent,
  type TurnEvent,
  type VisitorMessage,
  type VisitorSession,
} from './types';

type Activity = 'thinking' | 'searching';

export interface StreamingAnswer {
  // The text of the step being written, so a note written before a tool call never stays in the answer.
  text: string;
  // That note, shown as a status line while the tools run; the next step's text replaces it.
  status: string | null;
  step: number | null;
  citations: Citation[];
  activity: Activity | null;
  error: string | null;
  done: boolean;
}

// A message this tab sent: shown at once, then replaced by the stored copy.
export interface PendingTurn {
  id: number;
  question: string;
  // The stored copy's sequence, once it has arrived; until then the question shows from here.
  questionSequence: number | null;
  // The answer streamed by POST /messages; null until the stream opens, and when a person got the message.
  answer: StreamingAnswer | null;
}

type NoticeKind = 'rateLimited' | 'dailyCap' | 'busy' | 'offline' | 'error';

export interface Notice {
  kind: NoticeKind;
  // The API's own words, when it explained a refused message.
  text?: string;
}

export interface ChatState {
  phase: 'starting' | 'ready' | 'failed';
  visitor: { name: string | null; email: string | null };
  conversationId: string | null;
  status: ConversationStatus;
  messages: VisitorMessage[];
  conversationRating: number | null;
  pending: PendingTurn | null;
  // An answer being written that this tab did not start, seen on the live stream.
  followed: StreamingAnswer | null;
  notice: Notice | null;
  offline: boolean;
  // Actions of the latest answer that wait for Confirm or Cancel; a new message drops them.
  confirmations: Array<{ callId: string; title: string; details: ConfirmationDetail[] }>;
  // The latest answer offered a person; the visitor asks with the widget's own button.
  handoverOffered: boolean;
}

export interface ConfirmationAnswer {
  callId: string;
  approved: boolean;
}

export type SendResult = { status: 'sent' } | { status: 'failed' } | { status: 'needs-contact'; error: string };

export interface FieldErrors {
  name?: string;
  email?: string;
}

export type FormResult = { ok: true } | { ok: false; errors: FieldErrors };

export type ChatActivity = { kind: 'message'; message: VisitorMessage } | { kind: 'error'; text: string } | { kind: 'status'; text: string };

export interface ChatControllerOptions {
  storageKey: string;
  retryDelaysMs?: readonly number[];
  // The signed-in user of the host page; without it every session is anonymous.
  identity?: HostIdentity;
  // Replaces POST /sessions and the stored token, for the portal's preview of the real widget.
  session?: () => Promise<VisitorSession>;
}

const RETRY_DELAYS_MS: readonly number[] = [1_000, 2_000, 5_000, 10_000];

const RETRIEVAL_TOOLS = new Set(['search_knowledge_base', 'grep_sources', 'find_sources', 'read_source', 'delegate_research']);

function emptyAnswer(): StreamingAnswer {
  return { text: '', status: null, step: null, citations: [], activity: 'thinking', error: null, done: false };
}

const STATUS_LENGTH = 160;

// The last sentence of a note, as one plain line.
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

function appendStep(text: string, delta: string, newStep: boolean): string {
  if (!newStep || text.trim() === '' || /\n\s*$/.test(text) || /^\s*\n/.test(delta)) {
    return text + delta;
  }
  return `${text.trimEnd()}\n\n${delta.trimStart()}`;
}

function mergeCitations(existing: Citation[], incoming: Citation[]): Citation[] {
  const known = new Set(existing.map((citation) => citation.ref));
  return [...existing, ...incoming.filter((citation) => !known.has(citation.ref))];
}

function reduceAnswer(answer: StreamingAnswer, event: TurnEvent): StreamingAnswer {
  switch (event.type) {
    case 'tool-call':
      return { ...answer, activity: RETRIEVAL_TOOLS.has(event.toolName) ? 'searching' : 'thinking' };
    case 'tool-result':
      return { ...answer, citations: mergeCitations(answer.citations, event.citations), activity: answer.text ? null : 'thinking' };
    case 'text-delta': {
      // Without step-finished, as from an older server, steps would run together; a new step starts a paragraph.
      const newStep = event.step !== null && answer.step !== null && event.step !== answer.step;
      return { ...answer, text: appendStep(answer.text, event.text, newStep), step: event.step ?? answer.step, status: null, activity: null };
    }
    case 'step-finished':
      return event.kind === 'interim'
        ? { ...answer, text: '', step: null, status: statusLine(answer.text) ?? answer.status, activity: 'thinking' }
        : answer;
    case 'error':
      return { ...answer, error: event.message || null, status: null, activity: null };
    case 'done':
      return { ...answer, done: true, status: null, activity: null };
    default:
      return answer;
  }
}

function noticeFor(error: unknown): Notice {
  if (!(error instanceof WidgetApiError)) {
    return { kind: 'error' };
  }
  if (error.status === 0) {
    return { kind: 'offline' };
  }
  if (error.status === 429) {
    return { kind: error.code === DAILY_CAP_REACHED ? 'dailyCap' : 'rateLimited' };
  }
  if (error.code === CONVERSATION_BUSY) {
    return { kind: 'busy' };
  }
  const message = error.errorFor('message');
  return message ? { kind: 'error', text: message } : { kind: 'error' };
}

function readStoredToken(key: string): string | null {
  try {
    return window.localStorage.getItem(key);
  } catch {
    return null;
  }
}

function forgetToken(key: string): void {
  try {
    window.localStorage.removeItem(key);
  } catch {
    // Nothing stored.
  }
}

function storeToken(key: string, token: string): void {
  try {
    window.localStorage.setItem(key, token);
  } catch {
    // Storage can be blocked in third-party frames; the session then lasts as long as the page.
  }
}

function wait(ms: number, signal: AbortSignal): Promise<void> {
  return new Promise((resolve) => {
    const done = () => {
      window.clearTimeout(timer);
      signal.removeEventListener('abort', done);
      resolve();
    };
    const timer = window.setTimeout(done, ms);
    signal.addEventListener('abort', done, { once: true });
  });
}

const INITIAL_STATE: ChatState = {
  phase: 'starting',
  visitor: { name: null, email: null },
  conversationId: null,
  status: 'ai',
  messages: [],
  conversationRating: null,
  pending: null,
  followed: null,
  notice: null,
  offline: false,
  confirmations: [],
  handoverOffered: false,
};

export function initialChatState(): ChatState {
  return INITIAL_STATE;
}

// What the chat panel needs from a chat: ChatController talks to the API, the editor's preview plays sample scenes.
export interface ChatSession {
  readonly getState: () => ChatState;
  readonly subscribe: (listener: () => void) => () => void;
  onActivity(listener: (activity: ChatActivity) => void): () => void;
  readonly isAnswering: boolean;
  start(): Promise<void>;
  reconnect(): void;
  send(text: string, confirmation?: ConfirmationAnswer): Promise<SendResult>;
  newChat(): Promise<void>;
  endChat(): Promise<boolean>;
  saveContact(contact: { name: string; email: string }): Promise<FormResult>;
  requestHandover(contact?: { name: string; email: string }): Promise<FormResult>;
  // Stops waiting for a person; false when someone has joined meanwhile, or it failed.
  cancelHandover(): Promise<boolean>;
  rateAnswer(messageId: string, rating: 1 | -1, comment?: string): Promise<boolean>;
  rateConversation(rating: number): Promise<boolean>;
  dismissNotice(): void;
}

// The frame's chat state; components read it through useSyncExternalStore.
export class ChatController implements ChatSession {
  private state: ChatState = INITIAL_STATE;
  private readonly listeners = new Set<() => void>();
  private readonly activityListeners = new Set<(activity: ChatActivity) => void>();
  private readonly retryDelaysMs: readonly number[];
  private token: string | null = null;
  private expiresAt = Number.POSITIVE_INFINITY;
  private visitorId: string | null = null;
  private renewing: Promise<string> | null = null;
  private follow: { conversationId: string; abort: AbortController } | null = null;
  private sendAbort: AbortController | null = null;
  private lastSequence = 0;
  // Turns of this tab whose events may still arrive on the live stream, where they must not show a second time.
  private suppressedTurns = 0;
  private turnIds = 0;
  private disposed = false;
  // The host user the current session was started for, so identify() with the same user changes nothing.
  private sessionUser: string | null = null;
  private sessionStarted = false;
  private unsubscribeIdentity: (() => void) | null = null;

  constructor(
    private readonly api: WidgetApi,
    private readonly options: ChatControllerOptions,
  ) {
    this.retryDelaysMs = options.retryDelaysMs ?? RETRY_DELAYS_MS;
  }

  readonly getState = (): ChatState => this.state;

  readonly subscribe = (listener: () => void): (() => void) => {
    this.listeners.add(listener);
    return () => {
      this.listeners.delete(listener);
    };
  };

  // New messages from others and failed answers, for announcements and the unread count.
  onActivity(listener: (activity: ChatActivity) => void): () => void {
    this.activityListeners.add(listener);
    return () => {
      this.activityListeners.delete(listener);
    };
  }

  get isAnswering(): boolean {
    const { pending } = this.state;
    return pending !== null && !pending.answer?.done;
  }

  async start(): Promise<void> {
    this.set({ phase: 'starting', notice: null });
    const identity = this.options.identity;
    if (identity && !this.unsubscribeIdentity) {
      this.unsubscribeIdentity = identity.subscribe((command) => {
        // The first word from the loader only settles `ready`; the session below reads it.
        if (!this.sessionStarted) {
          return;
        }
        if (command.type === 'attributes') {
          void this.sendAttributes(command.attributes);
        } else if (command.type === 'logout') {
          void this.restart(true);
        } else if ((command.user?.userId ?? null) !== this.sessionUser) {
          void this.restart(false);
        }
      });
    }
    try {
      await identity?.ready;
      const session = await this.startSession();
      if (this.disposed) {
        return;
      }
      this.adoptSession(session);
      if (session.conversationId) {
        await this.openConversation(session.conversationId);
      }
      this.set({ phase: 'ready' });
    } catch (error) {
      if (!this.disposed) {
        this.set({ phase: 'failed', notice: noticeFor(error) });
      }
    }
  }

  dispose(): void {
    this.disposed = true;
    this.unsubscribeIdentity?.();
    this.sendAbort?.abort();
    this.stopFollowing();
    this.listeners.clear();
    this.activityListeners.clear();
  }

  dismissNotice(): void {
    if (this.state.notice) {
      this.set({ notice: null });
    }
  }

  // After the browser comes back online, reconnect at once instead of after the backoff.
  reconnect(): void {
    const conversationId = this.follow?.conversationId ?? this.state.conversationId;
    if (conversationId && this.state.phase === 'ready') {
      this.startFollowing(conversationId, true);
    }
  }

  async send(text: string, confirmation?: ConfirmationAnswer): Promise<SendResult> {
    const message = text.trim();
    if (!message || this.state.phase !== 'ready' || this.isAnswering || this.disposed) {
      return { status: 'failed' };
    }
    if (this.state.status === 'closed') {
      this.resetConversation();
    }
    const id = ++this.turnIds;
    this.suppressedTurns += 1;
    this.set({ pending: { id, question: message, questionSequence: null, answer: null }, notice: null, confirmations: [], handoverOffered: false });

    let renewed = false;
    let restarted = false;
    for (;;) {
      const conversationId = this.state.conversationId;
      const abort = new AbortController();
      this.sendAbort = abort;
      let streaming = false;
      try {
        const token = await this.ensureToken();
        const outcome = await this.api.sendMessage(
          token,
          conversationId ? { message, conversationId, ...(confirmation ? { confirmation } : {}) } : { message },
          abort.signal,
        );
        if (outcome.kind === 'relayed') {
          this.suppressedTurns = Math.max(0, this.suppressedTurns - 1);
          if (outcome.message) {
            this.mergeMessages([outcome.message], false);
          }
          const status = this.state.status;
          this.set({ pending: null, status: status === 'human' || status === 'handover_pending' ? status : 'handover_pending' });
          if (!outcome.message && conversationId) {
            void this.reloadHistory(conversationId);
          }
          return { status: 'sent' };
        }
        streaming = true;
        this.updatePending(id, (pending) => ({ ...pending, answer: emptyAnswer() }));
        for await (const event of outcome.events) {
          if (abort.signal.aborted || this.state.pending?.id !== id) {
            return { status: 'sent' };
          }
          this.applyOwnEvent(id, event);
        }
        await this.finishOwnTurn(id);
        return { status: 'sent' };
      } catch (error) {
        if (abort.signal.aborted || isAbortError(error) || this.disposed) {
          return { status: 'sent' };
        }
        if (streaming && this.state.conversationId) {
          this.onOwnStreamInterrupted(id);
          return { status: 'sent' };
        }
        if (isApiError(error, 401) && !renewed) {
          renewed = true;
          if (await this.tryRenewSession()) {
            continue;
          }
        }
        if (conversationId && !restarted && (isApiError(error, 404) || (isApiError(error, 409) && error.code === CONVERSATION_CLOSED))) {
          // The conversation is gone or was closed meanwhile: the message starts a new one.
          restarted = true;
          this.resetConversation();
          continue;
        }
        this.suppressedTurns = Math.max(0, this.suppressedTurns - 1);
        if (this.state.pending?.id === id) {
          this.set({ pending: null });
        }
        const emailError = isApiError(error, 400) ? error.errorFor('email') : undefined;
        if (emailError) {
          return { status: 'needs-contact', error: emailError };
        }
        this.set({ notice: noticeFor(error) });
        return { status: 'failed' };
      } finally {
        if (this.sendAbort === abort) {
          this.sendAbort = null;
        }
      }
    }
  }

  async newChat(): Promise<void> {
    const { conversationId, status } = this.state;
    this.sendAbort?.abort();
    this.sendAbort = null;
    this.resetConversation();
    this.set({ pending: null, notice: null });
    if (conversationId && status !== 'closed') {
      // Closed, so the next session does not resume it; when that fails the next message still starts a new one.
      try {
        await this.withSession((token) => this.api.closeConversation(token, conversationId));
      } catch {
        // Nothing to show: the visitor already sees the new chat.
      }
    }
  }

  async endChat(): Promise<boolean> {
    const { conversationId, status } = this.state;
    if (!conversationId || status === 'closed') {
      return false;
    }
    try {
      await this.withSession((token) => this.api.closeConversation(token, conversationId));
      if (this.state.conversationId === conversationId) {
        this.set({ status: 'closed', notice: null });
      }
      return true;
    } catch (error) {
      this.set({ notice: noticeFor(error) });
      return false;
    }
  }

  async saveContact(contact: { name: string; email: string }): Promise<FormResult> {
    try {
      const saved = await this.withSession((token) => this.api.saveContact(token, compactContact(contact)));
      this.set({ visitor: { name: saved.name, email: saved.email } });
      return { ok: true };
    } catch (error) {
      return this.formError(error);
    }
  }

  async requestHandover(contact?: { name: string; email: string }): Promise<FormResult> {
    const conversationId = this.state.conversationId;
    if (!conversationId) {
      return { ok: false, errors: {} };
    }
    this.set({ handoverOffered: false });
    try {
      const result = await this.withSession((token) => this.api.requestHandover(token, conversationId, contact ? compactContact(contact) : {}));
      if (contact) {
        const { name, email } = this.state.visitor;
        this.set({ visitor: { name: contact.name.trim() || name, email: contact.email.trim() || email } });
      }
      if (!result.started) {
        this.set({ notice: { kind: 'error' } });
        return { ok: false, errors: {} };
      }
      if (this.state.conversationId === conversationId) {
        const status = normalizeStatus(result.status);
        this.set({ status: status && status !== 'ai' ? status : 'handover_pending', notice: null });
      }
      return { ok: true };
    } catch (error) {
      if (isApiError(error, 409) && error.code === CONVERSATION_CLOSED) {
        this.set({ status: 'closed' });
        return { ok: false, errors: {} };
      }
      return this.formError(error);
    }
  }

  async cancelHandover(): Promise<boolean> {
    const conversationId = this.state.conversationId;
    if (!conversationId || this.state.status !== 'handover_pending') {
      return false;
    }
    try {
      await this.withSession((token) => this.api.cancelHandover(token, conversationId));
      // The live stream may have brought a newer status already, such as a person who joined right after.
      if (this.state.conversationId === conversationId && this.state.status === 'handover_pending') {
        this.set({ status: 'ai', notice: null });
      }
      return true;
    } catch (error) {
      if (isApiError(error, 409) && (error.code === PERSON_JOINED || error.code === CONVERSATION_CLOSED)) {
        // The live stream may have missed the change; the stored conversation has it, with the notice that says so.
        void this.reloadHistory(conversationId);
        return false;
      }
      this.set({ notice: noticeFor(error) });
      return false;
    }
  }

  async rateAnswer(messageId: string, rating: 1 | -1, comment?: string): Promise<boolean> {
    const conversationId = this.state.conversationId;
    if (!conversationId) {
      return false;
    }
    try {
      const body = comment?.trim() ? { rating, messageId, comment: comment.trim() } : { rating, messageId };
      await this.withSession((token) => this.api.sendFeedback(token, conversationId, body));
      this.set({ messages: this.state.messages.map((message) => (message.id === messageId ? { ...message, rating } : message)) });
      return true;
    } catch (error) {
      this.set({ notice: noticeFor(error) });
      return false;
    }
  }

  async rateConversation(rating: number): Promise<boolean> {
    const conversationId = this.state.conversationId;
    if (!conversationId) {
      return false;
    }
    try {
      await this.withSession((token) => this.api.sendFeedback(token, conversationId, { rating }));
      this.set({ conversationRating: rating });
      return true;
    } catch (error) {
      this.set({ notice: noticeFor(error) });
      return false;
    }
  }

  private formError(error: unknown): FormResult {
    if (isApiError(error, 400)) {
      const errors: FieldErrors = {};
      const name = error.errorFor('name');
      const email = error.errorFor('email');
      if (name) {
        errors.name = name;
      }
      if (email) {
        errors.email = email;
      }
      if (name || email) {
        return { ok: false, errors };
      }
    }
    this.set({ notice: noticeFor(error) });
    return { ok: false, errors: {} };
  }

  private set(patch: Partial<ChatState>): void {
    if (this.disposed) {
      return;
    }
    this.state = { ...this.state, ...patch };
    if (this.state.handoverOffered && this.state.status !== 'ai') {
      this.state = { ...this.state, handoverOffered: false };
    }
    for (const listener of [...this.listeners]) {
      listener();
    }
  }

  private emit(activity: ChatActivity): void {
    for (const listener of [...this.activityListeners]) {
      listener(activity);
    }
  }

  // The status line itself is not a live region, so it is announced once, here.
  private announceStatus(before: StreamingAnswer | null, after: StreamingAnswer | null): void {
    if (after?.status && after.status !== before?.status) {
      this.emit({ kind: 'status', text: after.status });
    }
  }

  private updatePending(id: number, update: (pending: PendingTurn) => PendingTurn): void {
    const pending = this.state.pending;
    if (pending?.id === id) {
      this.set({ pending: update(pending) });
    }
  }

  private adoptSession(session: VisitorSession): void {
    this.token = session.token;
    const expires = session.expiresAt ? Date.parse(session.expiresAt) : Number.NaN;
    this.expiresAt = Number.isNaN(expires) ? Number.POSITIVE_INFINITY : expires;
    this.visitorId = session.visitorId;
    if (!this.options.session) {
      storeToken(this.options.storageKey, session.token);
    }
    this.set({ visitor: { name: session.name, email: session.email } });
  }

  private async startSession(): Promise<VisitorSession> {
    if (this.options.session) {
      this.sessionStarted = true;
      return this.options.session();
    }
    const identity = this.options.identity;
    const user = identity?.current() ?? null;
    this.sessionUser = user?.userId ?? null;
    this.sessionStarted = true;
    const session = await this.api.startSession(this.token ?? readStoredToken(this.options.storageKey), user, {
      attributes: identity?.attributes() ?? {},
      page: identity?.page() ?? null,
    });
    warnAbout(session.rejectedAttributes ?? []);
    return session;
  }

  // update() on the host page while the chat is open; before a session they go with the next one.
  private async sendAttributes(attributes: Record<string, HostAttributeValue>): Promise<void> {
    if (!this.token || this.disposed || Object.keys(attributes).length === 0) {
      return;
    }
    try {
      warnAbout(await this.api.updateAttributes(await this.ensureToken(), attributes));
    } catch {
      // The next session sends them again.
    }
  }

  // Another user signed in on the host page, or the user signed out: a session for them, with their own chat.
  private async restart(forget: boolean): Promise<void> {
    if (this.disposed) {
      return;
    }
    this.sendAbort?.abort();
    if (forget) {
      this.token = null;
      this.visitorId = null;
      forgetToken(this.options.storageKey);
    }
    this.resetConversation();
    this.set({ pending: null, visitor: { name: null, email: null } });
    await this.start();
  }

  private async ensureToken(): Promise<string> {
    if (this.token && Date.now() < this.expiresAt) {
      return this.token;
    }
    return this.renewSession();
  }

  // A new session for the stored token; an expired token gets a new visitor, who cannot see the old conversation.
  private renewSession(): Promise<string> {
    this.renewing ??= (async () => {
      try {
        const previousVisitor = this.visitorId;
        const session = await this.startSession();
        this.adoptSession(session);
        if (previousVisitor && session.visitorId !== previousVisitor) {
          this.resetConversation();
          if (session.conversationId && !this.isAnswering) {
            void this.openConversation(session.conversationId);
          }
        }
        return session.token;
      } finally {
        this.renewing = null;
      }
    })();
    return this.renewing;
  }

  private async tryRenewSession(): Promise<boolean> {
    try {
      await this.renewSession();
      return true;
    } catch {
      return false;
    }
  }

  private async withSession<T>(call: (token: string) => Promise<T>): Promise<T> {
    const token = await this.ensureToken();
    try {
      return await call(token);
    } catch (error) {
      if (!isApiError(error, 401)) {
        throw error;
      }
      return call(await this.renewSession());
    }
  }

  private async openConversation(conversationId: string): Promise<void> {
    this.set({ conversationId });
    try {
      const history = await this.withSession((token) => this.api.getConversation(token, conversationId));
      if (this.state.conversationId !== conversationId) {
        return;
      }
      this.mergeMessages(history.messages, false);
      this.set({ status: history.status, conversationRating: history.rating });
      this.startFollowing(conversationId);
    } catch (error) {
      if (this.state.conversationId !== conversationId) {
        return;
      }
      if (isApiError(error, 404)) {
        this.resetConversation();
        return;
      }
      // The live stream replays the stored messages after sequence 0, so the history still shows.
      this.startFollowing(conversationId);
    }
  }

  private async reloadHistory(conversationId: string): Promise<boolean> {
    try {
      const history = await this.withSession((token) => this.api.getConversation(token, conversationId));
      if (this.state.conversationId !== conversationId) {
        return false;
      }
      this.mergeMessages(history.messages, true);
      this.set({ status: history.status, conversationRating: history.rating ?? this.state.conversationRating });
      return true;
    } catch (error) {
      if (isApiError(error, 404) && this.state.conversationId === conversationId) {
        this.resetConversation();
      }
      return false;
    }
  }

  private resetConversation(): void {
    this.stopFollowing();
    this.lastSequence = 0;
    this.suppressedTurns = this.sendAbort ? 1 : 0;
    this.set({
      conversationId: null,
      status: 'ai',
      messages: [],
      conversationRating: null,
      followed: null,
      offline: false,
      confirmations: [],
      handoverOffered: false,
    });
  }

  private mergeMessages(incoming: readonly VisitorMessage[], announce: boolean): void {
    if (incoming.length === 0) {
      return;
    }
    const before = this.lastSequence;
    const bySequence = new Map(this.state.messages.map((message) => [message.sequence, message] as const));
    for (const message of incoming) {
      const existing = bySequence.get(message.sequence);
      bySequence.set(message.sequence, existing ? { ...message, rating: message.rating ?? existing.rating } : message);
    }
    this.lastSequence = Math.max(before, ...incoming.map((message) => message.sequence));

    let pending = this.state.pending;
    if (pending && pending.questionSequence === null) {
      const question = pending.question;
      const stored = incoming.find((message) => message.author === 'visitor' && message.sequence > before && message.text.trim() === question);
      if (stored) {
        pending = { ...pending, questionSequence: stored.sequence };
      }
    }
    this.set({ messages: [...bySequence.values()].sort((a, b) => a.sequence - b.sequence), pending });

    if (announce) {
      for (const message of incoming) {
        if (message.sequence > before && message.author !== 'visitor') {
          this.emit({ kind: 'message', message });
        }
      }
    }
  }

  private applyOwnEvent(id: number, event: TurnEvent): void {
    if ((event.type === 'conversation' || event.type === 'done') && event.conversationId && !this.state.conversationId) {
      this.set({ conversationId: event.conversationId });
      this.startFollowing(event.conversationId);
    }
    if (event.type === 'error' && event.message) {
      this.emit({ kind: 'error', text: event.message });
    }
    if (event.type === 'confirmation-required') {
      const { callId, title, details } = event;
      this.set({ confirmations: [...this.state.confirmations.filter((item) => item.callId !== callId), { callId, title, details }] });
    }
    if (event.type === 'handover-offered') {
      this.set({ handoverOffered: true });
    }
    const before = this.state.pending?.id === id ? this.state.pending.answer : null;
    this.updatePending(id, (pending) => ({ ...pending, answer: reduceAnswer(pending.answer ?? emptyAnswer(), event) }));
    this.announceStatus(before, this.state.pending?.answer ?? null);
  }

  private async finishOwnTurn(id: number): Promise<void> {
    this.updatePending(id, (pending) => (pending.answer ? { ...pending, answer: { ...pending.answer, done: true, activity: null } } : pending));
    const conversationId = this.state.conversationId;
    // The stored answer has an id for feedback; when it cannot be loaded, the streamed one stays.
    if (!conversationId || !(await this.reloadHistory(conversationId))) {
      return;
    }
    if (this.state.pending?.id === id) {
      this.set({ pending: null });
    }
  }

  // The answer goes on without this tab: drop the streamed part and follow the turn from its start on the live stream.
  private onOwnStreamInterrupted(id: number): void {
    this.suppressedTurns = 0;
    if (this.state.pending?.id === id) {
      this.set({ pending: null });
    }
    const conversationId = this.state.conversationId;
    if (conversationId) {
      this.startFollowing(conversationId, true);
      void this.reloadHistory(conversationId);
    }
  }

  private startFollowing(conversationId: string, restart = false): void {
    if (this.disposed || (!restart && this.follow?.conversationId === conversationId)) {
      return;
    }
    this.stopFollowing();
    const abort = new AbortController();
    this.follow = { conversationId, abort };
    void this.followLoop(conversationId, abort.signal);
  }

  private stopFollowing(): void {
    this.follow?.abort.abort();
    this.follow = null;
  }

  private async followLoop(conversationId: string, signal: AbortSignal): Promise<void> {
    try {
      await this.followUntilStopped(conversationId, signal);
    } finally {
      // A loop that gave up (a refused request) must not block following the same conversation again.
      if (this.follow?.abort.signal === signal) {
        this.follow = null;
      }
    }
  }

  private async followUntilStopped(conversationId: string, signal: AbortSignal): Promise<void> {
    let failures = 0;
    let resume = false;
    while (!signal.aborted) {
      try {
        const token = await this.ensureToken();
        const events = this.api.followConversation(token, conversationId, {
          after: this.lastSequence,
          resume,
          signal,
          onOpen: () => {
            failures = 0;
            this.onLiveOpen();
          },
        });
        for await (const event of events) {
          if (signal.aborted) {
            return;
          }
          this.onLiveEvent(event);
        }
      } catch (error) {
        if (signal.aborted || isAbortError(error)) {
          return;
        }
        if (isApiError(error, 401)) {
          if (!(await this.tryRenewSession()) || signal.aborted) {
            return;
          }
        } else if (isApiError(error) && error.status >= 400 && error.status < 500 && error.status !== 408 && error.status !== 429) {
          // Gone (404) or refused: another try would fail the same way.
          return;
        }
        failures += 1;
        if (failures >= 2 && !this.state.offline) {
          this.set({ offline: true });
        }
      }
      // Also when the server ended the stream, such as on a restart.
      resume = true;
      const delay = this.retryDelaysMs[Math.min(Math.max(failures - 1, 0), this.retryDelaysMs.length - 1)] ?? 1_000;
      await wait(delay, signal);
    }
  }

  private onLiveOpen(): void {
    // A connection starts with the turn being written from its first event, and only this tab's current one can be it.
    this.suppressedTurns = this.sendAbort ? 1 : 0;
    if (this.state.offline || this.state.followed) {
      this.set({ offline: false, followed: null });
    }
  }

  private onLiveEvent(event: LiveEvent): void {
    switch (event.type) {
      case 'message': {
        const message = event.message;
        if (message.sequence <= this.lastSequence) {
          return;
        }
        // Only one turn runs at a time, so a question from elsewhere means this tab's turns are over.
        const ownQuestion = this.state.pending?.question === message.text.trim();
        if (message.author === 'visitor' && !this.sendAbort && !ownQuestion) {
          this.suppressedTurns = 0;
        }
        if (message.author === 'assistant' && this.state.followed) {
          this.set({ followed: null });
        }
        this.mergeMessages([message], true);
        return;
      }
      case 'turn': {
        if (this.suppressedTurns > 0) {
          if (event.event.type === 'done') {
            this.suppressedTurns -= 1;
          }
          return;
        }
        if (event.event.type === 'done') {
          if (this.state.followed) {
            this.set({ followed: null });
          }
          return;
        }
        if (event.event.type === 'error' && event.event.message) {
          this.emit({ kind: 'error', text: event.event.message });
        }
        const before = this.state.followed;
        this.set({ followed: reduceAnswer(before ?? emptyAnswer(), event.event) });
        this.announceStatus(before, this.state.followed);
        return;
      }
      case 'status':
        if (event.status !== this.state.status) {
          this.set({ status: event.status });
        }
        return;
    }
  }
}

function compactContact(contact: { name: string; email: string }): { name?: string; email?: string } {
  const result: { name?: string; email?: string } = {};
  if (contact.name.trim()) {
    result.name = contact.name.trim();
  }
  if (contact.email.trim()) {
    result.email = contact.email.trim();
  }
  return result;
}

// For the host page's developer: the frame's console is the page's console.
function warnAbout(rejected: readonly AttributeRejection[]): void {
  for (const item of rejected) {
    console.warn(`Knowledge Agent: attribute "${item.key}" was not saved. ${item.reason}`);
  }
}
