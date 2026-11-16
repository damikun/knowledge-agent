export const PREVIEW_SOURCE = 'knowledge-agent-preview';
export const PREVIEW_VERSION = 1;

export const PREVIEW_SCENES = ['launcher', 'start', 'answering', 'conversation', 'contact', 'waiting', 'joined', 'ended'] as const;
export type PreviewScene = (typeof PREVIEW_SCENES)[number];

export type ColorScheme = 'light' | 'dark';

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

export interface PreviewBox {
  x: number;
  y: number;
  width: number;
  height: number;
}

export type CornerRadii = readonly [topLeft: number, topRight: number, bottomRight: number, bottomLeft: number];

export interface PreviewRect extends PreviewBox {
  radii?: CornerRadii;
  panel?: PreviewBox;
}

export type EditorMessage =
  | { type: 'render'; config: unknown; scene: PreviewScene; colorScheme: ColorScheme }
  | { type: 'track'; target: EditTarget | null; fallbacks: EditTarget[] }
  | { type: 'live'; widgetId: string; colorScheme: ColorScheme };

export type PreviewMessage =
  | { type: 'ready' }
  | { type: 'rendered'; scene: PreviewScene; targets: EditTarget[] }
  | { type: 'hover'; target: EditTarget | null; rect: PreviewRect | null }
  | { type: 'select'; target: EditTarget | null; rect: PreviewRect | null }
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

function isLength(value: unknown): value is number {
  return typeof value === 'number' && Number.isFinite(value);
}

function readBox(value: unknown): PreviewBox | undefined {
  if (!isRecord(value)) {
    return undefined;
  }
  const { x, y, width, height } = value;
  if (!isLength(x) || !isLength(y) || !isLength(width) || !isLength(height) || width < 0 || height < 0) {
    return undefined;
  }
  return { x, y, width, height };
}

function readRadii(value: unknown): CornerRadii | undefined {
  return Array.isArray(value) && value.length === 4 && value.every((radius) => isLength(radius) && radius >= 0)
    ? [value[0], value[1], value[2], value[3]]
    : undefined;
}

function readRect(value: unknown): PreviewRect | null | undefined {
  if (value === null) {
    return null;
  }
  const box = readBox(value);
  if (!box || !isRecord(value)) {
    return undefined;
  }
  const radii = readRadii(value.radii);
  const panel = readBox(value.panel);
  return { ...box, ...(radii ? { radii } : {}), ...(panel ? { panel } : {}) };
}

function readTarget(value: unknown): EditTarget | null | undefined {
  return value === null ? null : isEditTarget(value) ? value : undefined;
}

function payload(data: unknown): Record<string, unknown> | null {
  return isRecord(data) && data.source === PREVIEW_SOURCE && data.version === PREVIEW_VERSION ? data : null;
}

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
