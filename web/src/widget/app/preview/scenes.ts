import type { PreviewScene } from '../../shared/preview';
import { initialChatState, type ChatSession, type ChatState, type FormResult, type SendResult } from '../controller';
import type { VisitorMessage, WidgetConfig } from '../types';

// Sample conversations for the editor preview. Every text the widget shows still comes from the edited config.

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
  const base: ChatState = { ...initialChatState(), phase: 'ready', visitor: SAMPLE_VISITOR };
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
            step: null,
            citations: ANSWER.citations,
            activity: null,
            error: null,
            done: false,
          },
        },
      };
    case 'contact':
      return { ...base, visitor: { name: null, email: null } };
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

// The contact form scene shows the form whatever the widget's setting, so its texts can be edited.
export function sceneConfig(config: WidgetConfig, scene: ChatScene): WidgetConfig {
  return scene === 'contact' ? { ...config, contactCapture: 'before_chat' } : config;
}

const FAILED: FormResult = { ok: false, errors: {} };
const NOT_SENT: SendResult = { status: 'failed' };

// A chat that stays in one scene: what the visitor would do is not sent anywhere.
export class SceneSession implements ChatSession {
  constructor(private readonly state: ChatState) {}

  get isAnswering(): boolean {
    const { pending } = this.state;
    return pending !== null && !pending.answer?.done;
  }

  readonly getState = (): ChatState => this.state;

  readonly subscribe = (): (() => void) => () => undefined;

  onActivity(): () => void {
    return () => undefined;
  }

  start(): Promise<void> {
    return Promise.resolve();
  }

  reconnect(): void {}

  send(): Promise<SendResult> {
    return Promise.resolve(NOT_SENT);
  }

  newChat(): Promise<void> {
    return Promise.resolve();
  }

  endChat(): Promise<boolean> {
    return Promise.resolve(false);
  }

  saveContact(): Promise<FormResult> {
    return Promise.resolve(FAILED);
  }

  requestHandover(): Promise<FormResult> {
    return Promise.resolve(FAILED);
  }

  cancelHandover(): Promise<boolean> {
    return Promise.resolve(false);
  }

  rateAnswer(): Promise<boolean> {
    return Promise.resolve(false);
  }

  rateConversation(): Promise<boolean> {
    return Promise.resolve(false);
  }

  dismissNotice(): void {}
}
