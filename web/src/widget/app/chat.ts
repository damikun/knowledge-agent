import { createStore, type StoreApi } from 'zustand/vanilla';
import { isAbortError } from '../../lib/sse';
import { CONVERSATION_CLOSED, isApiError, PERSON_JOINED, type Contact, type WidgetApi } from './api';
import {
  emptyAnswer,
  INITIAL_CHAT,
  isAnswering,
  lastSequence,
  mergeMessages,
  NO_CONVERSATION,
  reduceAnswer,
  type Chat,
  type ChatActivity,
  type ConfirmationAnswer,
  type ContactInput,
  type PendingTurn,
  type SendResult,
  type StreamingAnswer,
} from './chatState';
import { contactErrors, noticeFor, quietly, type FormResult } from './errors';
import { followConversation, type LiveFeed } from './liveFeed';
import { normalizeStatus, type LiveEvent, type TurnEvent, type VisitorMessage } from './types';
import type { VisitorState, VisitorStore } from './visitor';

export interface ChatOptions {
  retryDelaysMs?: readonly number[] | undefined;
}

const RETRY_DELAYS_MS: readonly number[] = [1_000, 2_000, 5_000, 10_000];
const SENT: SendResult = { status: 'sent' };
const NOT_SENT: SendResult = { status: 'failed' };
const DONE: FormResult = { ok: true };
const NOT_DONE: FormResult = { ok: false, errors: {} };

export function createChatStore(api: WidgetApi, visitor: VisitorStore, lifetime: AbortSignal, options: ChatOptions = {}): StoreApi<Chat> {
  const retryDelaysMs = options.retryDelaysMs ?? RETRY_DELAYS_MS;
  let conversation = new AbortController();
  let live = new AbortController();
  let listing = new AbortController();
  let answer: AbortController | null = null;
  let turns = 0;
  let ownTurnsOnFeed = 0;

  const store = createStore<Chat>()(() => ({
    ...INITIAL_CHAT,
    retry,
    reconnect,
    send,
    newChat,
    loadConversations,
    showConversation,
    endChat,
    saveContact,
    requestHandover,
    cancelHandover,
    rateAnswer,
    rateConversation,
    dismissNotice,
  }));
  const { getState: get, setState: set } = store;

  const feed: LiveFeed = {
    after: () => lastSequence(get()),
    onOpen() {
      ownTurnsOnFeed = answer ? 1 : 0;
      if (get().offline || get().followed) {
        set({ offline: false, followed: null });
      }
    },
    onOffline() {
      if (!get().offline) {
        set({ offline: true });
      }
    },
    onEvent: onLiveEvent,
  };

  store.subscribe((state) => {
    if (state.handoverOffered && state.status !== 'ai') {
      set({ handoverOffered: false });
    }
  });

  const unwatchVisitor = visitor.subscribe((current, previous) => {
    if (current.phase !== previous.phase || current.visitorId !== previous.visitorId) {
      onVisitor(current);
    }
  });

  lifetime.addEventListener('abort', () => {
    unwatchVisitor();
    stopAnswer();
    conversation.abort();
    live.abort();
    listing.abort();
  });

  async function retry(): Promise<void> {
    if (visitor.getState().phase === 'failed') {
      await visitor.getState().start();
    }
  }

  function reconnect(): void {
    const { conversationId, phase } = get();
    if (conversationId && phase === 'ready') {
      follow(conversationId);
    }
  }

  async function send(text: string, confirmation?: ConfirmationAnswer): Promise<SendResult> {
    const message = text.trim();
    const state = get();
    if (!message || state.phase !== 'ready' || isAnswering(state) || lifetime.aborted) {
      return NOT_SENT;
    }
    if (state.status === 'closed') {
      resetConversation();
    }
    const id = ++turns;
    const turn = new AbortController();
    answer = turn;
    ownTurnsOnFeed += 1;
    set({ pending: { id, question: message, questionSequence: null, answer: null }, notice: null, confirmations: [], handoverOffered: false });
    try {
      return await streamAnswer(id, message, confirmation, turn.signal);
    } finally {
      if (answer === turn) {
        answer = null;
      }
    }
  }

  async function newChat(): Promise<void> {
    const { conversationId, status } = get();
    stopAnswer();
    resetConversation();
    set({ pending: null, notice: null });
    if (conversationId) {
      if (status !== 'closed') {
        await quietly(api.closeConversation(conversationId));
      }
      loadConversations();
    }
  }

  function loadConversations(): void {
    listing.abort();
    listing = new AbortController();
    void quietly(api.listConversations(listing.signal).then((conversations) => set({ conversations })));
  }

  async function showConversation(conversationId: string): Promise<void> {
    const state = get();
    if (state.phase !== 'ready' || isAnswering(state) || state.conversationId === conversationId) {
      return;
    }
    resetConversation();
    set({ pending: null, notice: null });
    await openConversation(conversationId);
  }

  async function endChat(): Promise<boolean> {
    const { conversationId, status } = get();
    if (!conversationId || status === 'closed') {
      return false;
    }
    try {
      await api.closeConversation(conversationId, conversation.signal);
    } catch (error) {
      return failed(error);
    }
    set({ status: 'closed', notice: null });
    return true;
  }

  async function saveContact(contact: ContactInput): Promise<FormResult> {
    try {
      await visitor.getState().saveContact(compact(contact));
      return DONE;
    } catch (error) {
      return formError(error);
    }
  }

  async function requestHandover(contact?: ContactInput): Promise<FormResult> {
    const { conversationId } = get();
    if (!conversationId) {
      return NOT_DONE;
    }
    set({ handoverOffered: false });
    const details = contact ? compact(contact) : {};
    try {
      const result = await api.requestHandover(conversationId, details, conversation.signal);
      if (contact) {
        visitor.getState().rememberContact(details);
      }
      if (!result.started) {
        set({ notice: { kind: 'error' } });
        return NOT_DONE;
      }
      const status = normalizeStatus(result.status);
      set({ status: status && status !== 'ai' ? status : 'handover_pending', notice: null });
      return DONE;
    } catch (error) {
      if (isApiError(error, 409) && error.code === CONVERSATION_CLOSED) {
        set({ status: 'closed' });
        return NOT_DONE;
      }
      return formError(error);
    }
  }

  async function cancelHandover(): Promise<boolean> {
    const { conversationId, status } = get();
    if (!conversationId || status !== 'handover_pending') {
      return false;
    }
    try {
      await api.cancelHandover(conversationId, conversation.signal);
    } catch (error) {
      if (isApiError(error, 409) && (error.code === PERSON_JOINED || error.code === CONVERSATION_CLOSED)) {
        void reloadHistory(conversationId);
        return false;
      }
      return failed(error);
    }
    if (get().status === 'handover_pending') {
      set({ status: 'ai', notice: null });
    }
    return true;
  }

  async function rateAnswer(messageId: string, rating: 1 | -1, comment?: string): Promise<boolean> {
    const { conversationId } = get();
    if (!conversationId) {
      return false;
    }
    const note = comment?.trim();
    try {
      await api.sendFeedback(conversationId, note ? { rating, messageId, comment: note } : { rating, messageId }, conversation.signal);
    } catch (error) {
      return failed(error);
    }
    set({ messages: get().messages.map((message) => (message.id === messageId ? { ...message, rating } : message)) });
    return true;
  }

  async function rateConversation(rating: number): Promise<boolean> {
    const { conversationId } = get();
    if (!conversationId) {
      return false;
    }
    try {
      await api.sendFeedback(conversationId, { rating }, conversation.signal);
    } catch (error) {
      return failed(error);
    }
    set({ conversationRating: rating });
    return true;
  }

  function dismissNotice(): void {
    if (get().notice) {
      set({ notice: null });
    }
  }

  function onVisitor(current: VisitorState): void {
    leaveVisitor();
    if (current.phase === 'ready' && current.visitorId) {
      void enter(current.visitorId, current.conversationId);
    } else if (current.phase === 'failed') {
      set({ phase: 'failed', notice: current.notice });
    } else {
      set({ phase: 'starting', notice: null });
    }
  }

  async function enter(visitorId: string, conversationId: string | null): Promise<void> {
    const { signal } = visitor.getState();
    set({ visitorId, phase: 'starting', notice: null });
    if (conversationId) {
      await openConversation(conversationId);
    }
    if (!signal.aborted && get().visitorId === visitorId) {
      set({ phase: 'ready' });
      loadConversations();
    }
  }

  function leaveVisitor(): void {
    stopAnswer();
    listing.abort();
    resetConversation();
    set({ visitorId: null, pending: null, conversations: null });
  }

  async function streamAnswer(id: number, message: string, confirmation: ConfirmationAnswer | undefined, signal: AbortSignal): Promise<SendResult> {
    let restarted = false;
    for (;;) {
      const { conversationId } = get();
      let streaming = false;
      try {
        const outcome = await api.sendMessage(
          conversationId ? { message, conversationId, ...(confirmation ? { confirmation } : {}) } : { message },
          signal,
        );
        if (outcome.kind === 'relayed') {
          relayed(outcome.message, conversationId);
          return SENT;
        }
        streaming = true;
        updatePending(id, (pending) => ({ ...pending, answer: emptyAnswer() }));
        for await (const event of outcome.events) {
          if (signal.aborted) {
            return SENT;
          }
          applyOwnEvent(id, event);
        }
        await finishOwnTurn(id);
        return SENT;
      } catch (error) {
        if (signal.aborted || isAbortError(error)) {
          return SENT;
        }
        const current = get().conversationId;
        if (streaming && current) {
          ownStreamInterrupted(id, current);
          return SENT;
        }
        if (conversationId && !restarted && isGone(error)) {
          restarted = true;
          resetConversation();
          continue;
        }
        ownTurnsOnFeed = Math.max(0, ownTurnsOnFeed - 1);
        clearPending(id);
        const emailError = isApiError(error, 400) ? error.errorFor('email') : undefined;
        if (emailError) {
          return { status: 'needs-contact', error: emailError };
        }
        showError(error);
        return NOT_SENT;
      }
    }
  }

  function relayed(message: VisitorMessage | null, conversationId: string | null): void {
    ownTurnsOnFeed = Math.max(0, ownTurnsOnFeed - 1);
    if (message) {
      addMessages([message], false);
    }
    const { status } = get();
    set({ pending: null, status: status === 'human' || status === 'handover_pending' ? status : 'handover_pending' });
    if (!message && conversationId) {
      void reloadHistory(conversationId);
    }
  }

  function applyOwnEvent(id: number, event: TurnEvent): void {
    if ((event.type === 'conversation' || event.type === 'done') && event.conversationId && !get().conversationId) {
      set({ conversationId: event.conversationId });
      follow(event.conversationId);
    }
    if (event.type === 'error' && event.message) {
      emit({ kind: 'error', text: event.message });
    }
    if (event.type === 'confirmation-required') {
      const { callId, title, details } = event;
      set({ confirmations: [...get().confirmations.filter((item) => item.callId !== callId), { callId, title, details }] });
    }
    if (event.type === 'handover-offered') {
      set({ handoverOffered: true });
    }
    const pending = get().pending;
    if (pending?.id === id) {
      const next = reduceAnswer(pending.answer ?? emptyAnswer(), event);
      set({ pending: { ...pending, answer: next } });
      announceStatus(pending.answer, next);
    }
  }

  async function finishOwnTurn(id: number): Promise<void> {
    updatePending(id, (pending) => (pending.answer ? { ...pending, answer: { ...pending.answer, done: true, activity: null } } : pending));
    const { conversationId } = get();
    if (conversationId && (await reloadHistory(conversationId))) {
      clearPending(id);
    }
  }

  function ownStreamInterrupted(id: number, conversationId: string): void {
    ownTurnsOnFeed = 0;
    clearPending(id);
    follow(conversationId);
    void reloadHistory(conversationId);
  }

  async function openConversation(conversationId: string): Promise<void> {
    set({ conversationId });
    const { signal } = conversation;
    try {
      const history = await api.getConversation(conversationId, signal);
      addMessages(history.messages, false);
      set({ status: history.status, conversationRating: history.rating });
    } catch (error) {
      if (signal.aborted || isAbortError(error)) {
        return;
      }
      if (isApiError(error, 404)) {
        resetConversation();
        return;
      }
    }
    follow(conversationId);
  }

  async function reloadHistory(conversationId: string): Promise<boolean> {
    const { signal } = conversation;
    try {
      const history = await api.getConversation(conversationId, signal);
      addMessages(history.messages, true);
      set({ status: history.status, conversationRating: history.rating ?? get().conversationRating });
      return true;
    } catch (error) {
      if (!signal.aborted && isApiError(error, 404)) {
        resetConversation();
      }
      return false;
    }
  }

  function resetConversation(): void {
    conversation.abort();
    conversation = new AbortController();
    live.abort();
    ownTurnsOnFeed = answer ? 1 : 0;
    set(NO_CONVERSATION);
  }

  function follow(conversationId: string): void {
    live.abort();
    live = new AbortController();
    void followConversation(api, conversationId, feed, retryDelaysMs, live.signal);
  }

  function onLiveEvent(event: LiveEvent): void {
    const state = get();
    switch (event.type) {
      case 'message': {
        const { message } = event;
        if (message.sequence <= lastSequence(state)) {
          return;
        }
        if (message.author === 'visitor' && !answer && state.pending?.question !== message.text.trim()) {
          ownTurnsOnFeed = 0;
        }
        if (message.author === 'assistant' && state.followed) {
          set({ followed: null });
        }
        addMessages([message], true);
        return;
      }
      case 'turn':
        onLiveTurn(event.event);
        return;
      case 'status':
        if (event.status !== state.status) {
          set({ status: event.status });
        }
        return;
    }
  }

  function onLiveTurn(event: TurnEvent): void {
    if (ownTurnsOnFeed > 0) {
      if (event.type === 'done') {
        ownTurnsOnFeed -= 1;
      }
      return;
    }
    const before = get().followed;
    if (event.type === 'done') {
      if (before) {
        set({ followed: null });
      }
      return;
    }
    if (event.type === 'error' && event.message) {
      emit({ kind: 'error', text: event.message });
    }
    const after = reduceAnswer(before ?? emptyAnswer(), event);
    set({ followed: after });
    announceStatus(before, after);
  }

  function addMessages(incoming: readonly VisitorMessage[], announce: boolean): void {
    if (incoming.length === 0) {
      return;
    }
    const { messages, pending, added } = mergeMessages(get(), incoming);
    set({ messages, pending });
    if (announce) {
      for (const message of added) {
        if (message.author !== 'visitor') {
          emit({ kind: 'message', message });
        }
      }
    }
  }

  function updatePending(id: number, update: (pending: PendingTurn) => PendingTurn): void {
    const { pending } = get();
    if (pending?.id === id) {
      set({ pending: update(pending) });
    }
  }

  function clearPending(id: number): void {
    if (get().pending?.id === id) {
      set({ pending: null });
    }
  }

  function stopAnswer(): void {
    answer?.abort();
    answer = null;
  }

  function emit(activity: ChatActivity): void {
    set({ activity });
  }

  function announceStatus(before: StreamingAnswer | null, after: StreamingAnswer): void {
    if (after.status && after.status !== before?.status) {
      emit({ kind: 'status', text: after.status });
    }
  }

  function showError(error: unknown): void {
    if (!isAbortError(error)) {
      set({ notice: noticeFor(error) });
    }
  }

  function failed(error: unknown): false {
    showError(error);
    return false;
  }

  function formError(error: unknown): FormResult {
    const errors = contactErrors(error);
    if (errors) {
      return { ok: false, errors };
    }
    showError(error);
    return NOT_DONE;
  }

  return store;
}

function isGone(error: unknown): boolean {
  return isApiError(error, 404) || (isApiError(error, 409) && error.code === CONVERSATION_CLOSED);
}

function compact(contact: ContactInput): Contact {
  const result: Contact = {};
  if (contact.name.trim()) {
    result.name = contact.name.trim();
  }
  if (contact.email.trim()) {
    result.email = contact.email.trim();
  }
  return result;
}
