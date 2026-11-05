// The messages between the portal's widget editor and the widget preview (/widget/v1/preview). Both pages come from the
// portal's origin; each side checks the origin and the sending window before it parses a message here. Every message
// carries the protocol version, so a preview cached from an older build is ignored instead of misread.

export const PREVIEW_SOURCE = 'knowledge-agent-preview';
export const PREVIEW_VERSION = 1;

/** The states the preview can show, all with sample data. "launcher" is the closed widget. */
export const PREVIEW_SCENES = ['launcher', 'start', 'answering', 'conversation', 'contact', 'waiting', 'joined', 'ended'] as const;
export type PreviewScene = (typeof PREVIEW_SCENES)[number];

export type ColorScheme = 'light' | 'dark';

/** In preview mode the frame marks the elements an admin can edit with this attribute. */
export const EDIT_ATTRIBUTE = 'data-ka-edit';

export const EDIT_TARGETS = [
  'appearance.launcher',
  'appearance.header',
  'appearance.avatar',
  'appearance.title',
  'appearance.subtitle',
  'header.menu',
  'header.close',
  'behavior.messages',
  'messages.visitor',
  'messages.assistant',
  'behavior.greeting',
  'behavior.suggestedQuestions',
  'behavior.citations',
  'behavior.feedback',
  'behavior.handover',
  'behavior.placeholder',
  'behavior.contact',
  'status.waiting',
  'status.joined',
  'status.ended',
  'texts.poweredBy',
] as const;
export type EditTarget = (typeof EDIT_TARGETS)[number];

export type EditMark = { [EDIT_ATTRIBUTE]?: EditTarget };

/** In the preview's own viewport, in CSS pixels. */
export interface PreviewRect {
  x: number;
  y: number;
  width: number;
  height: number;
}

export type EditorMessage =
  // The whole state to show; `config` is the public widget config, which the frame normalizes like a loaded one.
  | { type: 'render'; config: unknown; scene: PreviewScene; colorScheme: ColorScheme }
  // Report the rectangle of this element from now on, or of the first fallback that shows while it does not.
  | { type: 'track'; target: EditTarget | null; fallbacks: EditTarget[] }
  // "Test this widget": the real widget, live. Sent again when only the color scheme changes; the conversation stays.
  | { type: 'live'; widgetId: string; colorScheme: ColorScheme };

export type PreviewMessage =
  | { type: 'ready' }
  // After each render and resize: the marked elements that show in the scene.
  | { type: 'rendered'; scene: PreviewScene; targets: EditTarget[] }
  | { type: 'hover'; target: EditTarget | null; rect: PreviewRect | null }
  // The admin clicked an element, or something that is not editable (null). The frame then tracks that element.
  | { type: 'select'; target: EditTarget | null; rect: PreviewRect | null }
  // Where the tracked element is: `anchor` is the element measured, the target or a fallback, null when none shows.
  | { type: 'rect'; target: EditTarget; anchor: EditTarget | null; rect: PreviewRect | null };

type Envelope<T> = T & { source: typeof PREVIEW_SOURCE; version: typeof PREVIEW_VERSION };

export function envelope<T extends EditorMessage | PreviewMessage>(message: T): Envelope<T> {
  return { source: PREVIEW_SOURCE, version: PREVIEW_VERSION, ...message };
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function isOneOf<T extends string>(options: readonly T[], value: unknown): value is T {
  return typeof value === 'string' && (options as readonly string[]).includes(value);
}

export function isEditTarget(value: unknown): value is EditTarget {
  return isOneOf(EDIT_TARGETS, value);
}

function readRect(value: unknown): PreviewRect | null | undefined {
  if (value === null) {
    return null;
  }
  if (!isRecord(value)) {
    return undefined;
  }
  const { x, y, width, height } = value;
  const numbers = [x, y, width, height];
  if (!numbers.every((n) => typeof n === 'number' && Number.isFinite(n)) || (width as number) < 0 || (height as number) < 0) {
    return undefined;
  }
  return { x: x as number, y: y as number, width: width as number, height: height as number };
}

function readTarget(value: unknown): EditTarget | null | undefined {
  return value === null ? null : isEditTarget(value) ? value : undefined;
}

function payload(data: unknown): Record<string, unknown> | null {
  return isRecord(data) && data.source === PREVIEW_SOURCE && data.version === PREVIEW_VERSION ? data : null;
}

/** A message the editor sent, or null for anything else. */
export function parseEditorMessage(data: unknown): EditorMessage | null {
  const message = payload(data);
  switch (message?.type) {
    case 'render': {
      const { config, scene, colorScheme } = message;
      return isRecord(config) && isOneOf(PREVIEW_SCENES, scene) && isOneOf(['light', 'dark'] as const, colorScheme)
        ? { type: 'render', config, scene, colorScheme }
        : null;
    }
    case 'track': {
      const target = readTarget(message.target);
      const { fallbacks } = message;
      return target === undefined || !Array.isArray(fallbacks) || !fallbacks.every(isEditTarget) ? null : { type: 'track', target, fallbacks };
    }
    case 'live': {
      const { widgetId, colorScheme } = message;
      return typeof widgetId === 'string' && /^[0-9a-f-]{36}$/i.test(widgetId) && isOneOf(['light', 'dark'] as const, colorScheme)
        ? { type: 'live', widgetId, colorScheme }
        : null;
    }
    default:
      return null;
  }
}

/** A message the preview sent, or null for anything else. */
export function parsePreviewMessage(data: unknown): PreviewMessage | null {
  const message = payload(data);
  switch (message?.type) {
    case 'ready':
      return { type: 'ready' };
    case 'rendered': {
      const { scene, targets } = message;
      return isOneOf(PREVIEW_SCENES, scene) && Array.isArray(targets) && targets.every(isEditTarget) ? { type: 'rendered', scene, targets } : null;
    }
    case 'hover':
    case 'select': {
      const target = readTarget(message.target);
      const rect = readRect(message.rect);
      if (target === undefined || rect === undefined) {
        return null;
      }
      return message.type === 'hover' ? { type: 'hover', target, rect } : { type: 'select', target, rect };
    }
    case 'rect': {
      const anchor = readTarget(message.anchor);
      const rect = readRect(message.rect);
      if (!isEditTarget(message.target) || anchor === undefined || rect === undefined || (anchor === null) !== (rect === null)) {
        return null;
      }
      return { type: 'rect', target: message.target, anchor, rect };
    }
    default:
      return null;
  }
}
