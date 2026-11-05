import { act, screen } from '@testing-library/react';
import { vi } from 'vitest';
import {
  EDIT_TARGETS,
  envelope,
  parseEditorMessage,
  type EditorMessage,
  type EditTarget,
  type PreviewMessage,
  type PreviewRect,
  type PreviewScene,
} from '../widget/shared/preview';

const PANEL: readonly EditTarget[] = [
  'appearance.launcher',
  'appearance.header',
  'appearance.avatar',
  'appearance.title',
  'appearance.subtitle',
  'header.close',
  'behavior.messages',
  'behavior.greeting',
  'texts.poweredBy',
];

const ANSWERED: readonly EditTarget[] = [...PANEL, 'header.menu', 'messages.visitor', 'messages.assistant', 'behavior.citations', 'behavior.feedback'];

// What the real preview shows in each scene, on a desktop; widget/app/preview checks the real one.
function shownIn(scene: PreviewScene, config: Record<string, unknown>): EditTarget[] {
  const scenes: Record<PreviewScene, readonly EditTarget[]> = {
    launcher: ['appearance.launcher'],
    start: [...PANEL, 'behavior.suggestedQuestions', 'behavior.placeholder'],
    answering: [...PANEL, 'header.menu', 'messages.visitor', 'messages.assistant', 'behavior.placeholder'],
    conversation: [...ANSWERED, 'behavior.handover', 'behavior.placeholder'],
    contact: [...PANEL, 'behavior.contact'],
    waiting: [...ANSWERED, 'status.waiting', 'behavior.placeholder'],
    joined: [...ANSWERED, 'status.joined', 'behavior.placeholder'],
    ended: [...ANSWERED, 'status.ended'],
  };
  const off = new Set<EditTarget>();
  if (!config.subtitle) {
    off.add('appearance.subtitle');
  }
  if (!config.handoverEnabled) {
    off.add('behavior.handover');
  }
  if (!config.feedbackEnabled) {
    off.add('behavior.feedback');
  }
  if (!Array.isArray(config.suggestedQuestions) || config.suggestedQuestions.length === 0) {
    off.add('behavior.suggestedQuestions');
  }
  if (config.showFooter === false) {
    off.add('texts.poweredBy');
  }
  return scenes[scene].filter((target) => !off.has(target));
}

function rectOf(target: EditTarget): PreviewRect {
  return { x: 300, y: 80 + EDIT_TARGETS.indexOf(target) * 12, width: 220, height: 24 };
}

export interface FakePreview {
  sent: EditorMessage[];
  lastRender: () => Extract<EditorMessage, { type: 'render' }> | undefined;
  click: (target: EditTarget | null) => void;
}

/**
 * Stands in for /widget/v1/preview, which jsdom does not load: it answers the editor as the real preview does, each
 * answer in a task of its own.
 */
export async function fakePreview(): Promise<FakePreview> {
  const frame = await screen.findByTitle<HTMLIFrameElement>('Widget preview');
  const preview = frame.contentWindow!;
  const sent: EditorMessage[] = [];
  let shown: EditTarget[] = [];
  let tracked: Extract<EditorMessage, { type: 'track' }> | null = null;
  let placed: EditTarget | null = null;
  const post = (message: PreviewMessage) =>
    act(() => {
      window.dispatchEvent(new MessageEvent('message', { data: envelope(message), origin: window.location.origin, source: preview }));
    });
  // As the real preview does after each render and track: the target, else the first fallback that shows.
  const place = (always: boolean) => {
    if (!tracked?.target) {
      return;
    }
    const anchor = [tracked.target, ...tracked.fallbacks].find((target) => shown.includes(target)) ?? null;
    if (always || anchor !== placed) {
      placed = anchor;
      post({ type: 'rect', target: tracked.target, anchor, rect: anchor ? rectOf(anchor) : null });
    }
  };
  vi.spyOn(preview, 'postMessage').mockImplementation((data) => {
    const message = parseEditorMessage(data);
    if (!message) {
      return;
    }
    sent.push(message);
    window.setTimeout(() => {
      if (message.type === 'render') {
        shown = shownIn(message.scene, message.config as Record<string, unknown>);
        post({ type: 'rendered', scene: message.scene, targets: shown });
        place(false);
      } else if (message.type === 'track') {
        tracked = message;
        place(true);
      }
    }, 0);
  });
  post({ type: 'ready' });
  return {
    sent,
    lastRender: () => sent.filter((message): message is Extract<EditorMessage, { type: 'render' }> => message.type === 'render').at(-1),
    click: (target) => {
      tracked = target ? { type: 'track', target, fallbacks: [] } : null;
      placed = target;
      post({ type: 'select', target, rect: target ? rectOf(target) : null });
    },
  };
}
