import { createStore } from 'zustand/vanilla';
import type { PreviewScene } from '../../shared/preview';
import { INITIAL_CHAT, type Chat, type ChatActions, type ChatState, type ChatStore } from '../chatState';
import type { ReadonlyStore } from '../store';
import type { VisitorMessage, WidgetConfig } from '../types';
import { STARTING, type VisitorState } from '../visitor';

export type ChatScene = Exclude<PreviewScene, 'launcher'>;

const SAMPLE_VISITOR = { name: 'Alex', email: 'alex@example.com' };

function message(sequence: number, author: VisitorMessage['author'], text: string, extra: Partial<VisitorMessage> = {}): VisitorMessage {
  return {
    id: `sample-${sequence}`,
    sequence,
    author,
    authorName: null,
    text,
    citations: [],
    createdAt: '2026-01-01T10:00:00Z',
    rating: null,
    ...extra,
  };
}

const QUESTION = message(1, 'visitor', 'Can I return something I bought online?');
const ANSWER = message(
  2,
  'assistant',
  'Yes. Unused items can go back within 30 days [S1]. Start the return from your order page and we email you a prepaid label [S2].',
  {
    citations: [
      { ref: 'S1', title: 'Returns policy', url: null },
      { ref: 'S2', title: 'Shipping and returns', url: 'https://example.com/help/returns' },
    ],
  },
);
const STAFF_NAME = 'Dana';
const JOINED = message(3, 'system', `${STAFF_NAME} joined the conversation.`);
const STAFF_REPLY = message(4, 'staff', 'Hi, I am Dana from the team. I can help with your return.', { authorName: STAFF_NAME });

export function sceneState(scene: ChatScene): ChatState {
  const base: ChatState = { ...INITIAL_CHAT, phase: 'ready' };
  const conversation = { conversationId: 'sample', messages: [QUESTION, ANSWER] };
  switch (scene) {
    case 'start':
      return base;
    case 'answering':
      return {
        ...base,
        conversationId: 'sample',
        messages: [QUESTION],
        pending: {
          id: 1,
          question: QUESTION.text,
          questionSequence: QUESTION.sequence,
          answer: {
            text: 'Yes. Unused items can go back within 30 days [S1]. Start the return from your',
            status: null,
            citations: ANSWER.citations,
            activity: null,
            error: null,
            done: false,
          },
        },
      };
    case 'contact':
      return base;
    case 'conversation':
      return { ...base, ...conversation };
    case 'waiting':
      return { ...base, ...conversation, status: 'handover_pending' };
    case 'joined':
      return { ...base, conversationId: 'sample', messages: [QUESTION, ANSWER, JOINED, STAFF_REPLY], status: 'human' };
    case 'ended':
      return { ...base, ...conversation, status: 'closed' };
  }
}

export function sceneVisitor(scene: ChatScene): ReadonlyStore<VisitorState> {
  const contact = scene === 'contact' ? { name: null, email: null } : SAMPLE_VISITOR;
  const state: VisitorState = { ...STARTING, phase: 'ready', visitorId: 'sample', ...contact };
  return createStore<VisitorState>()(() => state);
}

export function sceneConfig(config: WidgetConfig, scene: ChatScene): WidgetConfig {
  return scene === 'contact' ? { ...config, contactCapture: 'before_chat' } : config;
}

const IDLE: ChatActions = {
  retry: async () => undefined,
  reconnect: () => undefined,
  send: async () => ({ status: 'failed' }),
  newChat: async () => undefined,
  loadConversations: () => undefined,
  showConversation: async () => undefined,
  endChat: async () => false,
  saveContact: async () => ({ ok: false, errors: {} }),
  requestHandover: async () => ({ ok: false, errors: {} }),
  cancelHandover: async () => false,
  rateAnswer: async () => false,
  rateConversation: async () => false,
  dismissNotice: () => undefined,
};

export function sceneChat(state: ChatState): ChatStore {
  return createStore<Chat>()(() => ({ ...state, ...IDLE }));
}
