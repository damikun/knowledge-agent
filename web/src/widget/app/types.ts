// The public widget API as the frame sees it (/api/public/widgets/{key}), plus normalizers that keep the frame
// working when a field is missing, such as in an unsaved preview config.

import { DEFAULT_BRAND_COLOR, parseHexColor } from '../shared/color';
import { DEFAULT_PRESET, isThemePreset, readThemeColors, type ThemeColors, type ThemePreset } from '../shared/theme';

export type CitationMode = 'hidden' | 'titles' | 'links';
type ContactCapture = 'never' | 'before_handover' | 'before_chat';
type ThemeChoice = 'auto' | 'light' | 'dark';
export type ConversationStatus = 'ai' | 'handover_pending' | 'human' | 'closed';
type MessageAuthor = 'visitor' | 'assistant' | 'staff' | 'system';

export interface WidgetConfig {
  key: string;
  title: string;
  subtitle: string | null;
  avatarUrl: string | null;
  launcherIcon: string;
  launcherIconUrl: string | null;
  brandColor: string;
  // The mode; the look is the preset.
  theme: ThemeChoice;
  preset: ThemePreset;
  colors: ThemeColors;
  showFooter: boolean;
  position: 'right' | 'left';
  launcherLabel: string | null;
  greeting: string | null;
  suggestedQuestions: string[];
  placeholder: string | null;
  citationMode: CitationMode;
  feedbackEnabled: boolean;
  handoverEnabled: boolean;
  contactCapture: ContactCapture;
  texts: Record<string, string>;
  maxMessageLength: number;
  // Hosts answers may link to: "example.com" also allows www., "*.example.com" its subdomains.
  linkHosts: string[];
}

export interface VisitorSession {
  token: string;
  visitorId: string;
  conversationId: string | null;
  expiresAt: string | null;
  name: string | null;
  email: string | null;
  // Attributes the server did not store, with why; the frame warns about them in the console.
  rejectedAttributes?: AttributeRejection[];
}

export interface AttributeRejection {
  key: string;
  reason: string;
}

export function readRejections(value: unknown): AttributeRejection[] {
  return Array.isArray(value) ? value.filter(isRecord).map((item) => ({ key: String(item.key ?? ''), reason: String(item.reason ?? '') })) : [];
}

export interface Citation {
  ref: string;
  title: string;
  url: string | null;
}

export interface VisitorMessage {
  id: string;
  sequence: number;
  author: MessageAuthor;
  authorName: string | null;
  text: string;
  citations: Citation[];
  createdAt: string;
  rating: 1 | -1 | null;
}

export interface VisitorConversation {
  id: string;
  status: ConversationStatus;
  rating: number | null;
  messages: VisitorMessage[];
}

// Events of a turn, from POST /messages or from the `turn` events of the live stream.
export type TurnEvent =
  | { type: 'conversation'; conversationId: string }
  | { type: 'tool-call'; toolName: string }
  | { type: 'tool-result'; citations: Citation[] }
  // step numbers the model calls of a turn; older servers send none.
  | { type: 'text-delta'; text: string; step: number | null }
  // interim: tools run next, so the text so far was a note on the way, not the answer.
  | { type: 'step-finished'; step: number; kind: 'interim' | 'final' }
  | { type: 'error'; message: string; code: string | null }
  | { type: 'done'; conversationId: string }
  // An action the agent asked about, which runs only after the visitor presses Confirm.
  | { type: 'confirmation-required'; callId: string; title: string; details: ConfirmationDetail[] }
  // The agent offered a person; the widget shows its own button.
  | { type: 'handover-offered'; callId: string };

// One argument of an action, as the server put it.
export interface ConfirmationDetail {
  label: string;
  value: string;
}

function readDetails(value: unknown): ConfirmationDetail[] {
  return Array.isArray(value) ? value.filter(isRecord).map((item) => ({ label: String(item.label ?? ''), value: String(item.value ?? '') })) : [];
}

export type LiveEvent =
  { type: 'message'; message: VisitorMessage } | { type: 'turn'; event: TurnEvent } | { type: 'status'; status: ConversationStatus };

export function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function text(value: unknown): string | null {
  return typeof value === 'string' && value.trim() !== '' ? value : null;
}

function choice<T extends string>(value: unknown, options: readonly T[], fallback: T): T {
  return typeof value === 'string' && (options as readonly string[]).includes(value) ? (value as T) : fallback;
}

export function normalizeConfig(value: unknown): WidgetConfig | null {
  if (!isRecord(value)) {
    return null;
  }
  const texts: Record<string, string> = {};
  if (isRecord(value.texts)) {
    for (const [key, entry] of Object.entries(value.texts)) {
      if (typeof entry === 'string') {
        texts[key] = entry;
      }
    }
  }
  const maxLength = typeof value.maxMessageLength === 'number' && value.maxMessageLength > 0 ? Math.floor(value.maxMessageLength) : 2000;
  return {
    key: text(value.key) ?? '',
    title: text(value.title) ?? 'Chat',
    subtitle: text(value.subtitle),
    avatarUrl: text(value.avatarUrl),
    launcherIcon: text(value.launcherIcon) ?? 'message-circle',
    launcherIconUrl: text(value.launcherIconUrl),
    brandColor: parseHexColor(text(value.brandColor)) ? String(value.brandColor).trim() : DEFAULT_BRAND_COLOR,
    theme: choice(value.theme, ['auto', 'light', 'dark'], 'auto'),
    preset: isThemePreset(value.preset) ? value.preset : DEFAULT_PRESET,
    colors: readThemeColors(value.colors),
    showFooter: value.showFooter !== false,
    position: choice(value.position, ['right', 'left'], 'right'),
    launcherLabel: text(value.launcherLabel),
    greeting: text(value.greeting),
    suggestedQuestions: Array.isArray(value.suggestedQuestions)
      ? value.suggestedQuestions.filter((q): q is string => typeof q === 'string' && q.trim() !== '')
      : [],
    placeholder: text(value.placeholder),
    citationMode: choice(value.citationMode, ['hidden', 'titles', 'links'], 'titles'),
    feedbackEnabled: value.feedbackEnabled === true,
    handoverEnabled: value.handoverEnabled === true,
    contactCapture: choice(value.contactCapture, ['never', 'before_handover', 'before_chat'], 'never'),
    texts,
    maxMessageLength: maxLength,
    linkHosts: Array.isArray(value.linkHosts)
      ? value.linkHosts.filter((host): host is string => typeof host === 'string' && host.trim() !== '').map((host) => host.trim().toLowerCase())
      : [],
  };
}

// The live stream has sent "handoverpending" where the REST API says "handover_pending", so both are accepted.
export function normalizeStatus(value: unknown): ConversationStatus | null {
  if (typeof value !== 'string') {
    return null;
  }
  switch (value.toLowerCase().replace(/[^a-z]/g, '')) {
    case 'ai':
      return 'ai';
    case 'handoverpending':
      return 'handover_pending';
    case 'human':
      return 'human';
    case 'closed':
      return 'closed';
    default:
      return null;
  }
}

// Stored messages carry title/url, stream events sourceTitle/sourceUrl.
function normalizeCitations(value: unknown): Citation[] {
  if (!Array.isArray(value)) {
    return [];
  }
  return value.flatMap((item) => {
    if (!isRecord(item) || typeof item.ref !== 'string') {
      return [];
    }
    const title = text(item.title) ?? text(item.sourceTitle) ?? item.ref;
    const url = text(item.url) ?? text(item.sourceUrl);
    return [{ ref: item.ref.trim().toUpperCase(), title, url }];
  });
}

const AUTHORS: readonly MessageAuthor[] = ['visitor', 'assistant', 'staff', 'system'];

export function normalizeMessage(value: unknown): VisitorMessage | null {
  if (!isRecord(value) || typeof value.sequence !== 'number') {
    return null;
  }
  const author = value.author === 'user' ? 'visitor' : value.author;
  if (typeof author !== 'string' || !(AUTHORS as readonly string[]).includes(author)) {
    return null;
  }
  return {
    id: typeof value.id === 'string' ? value.id : `message-${value.sequence}`,
    sequence: value.sequence,
    author: author as MessageAuthor,
    authorName: text(value.authorName),
    text: typeof value.text === 'string' ? value.text : '',
    citations: normalizeCitations(value.citations),
    createdAt: typeof value.createdAt === 'string' ? value.createdAt : '',
    rating: value.rating === 1 || value.rating === -1 ? value.rating : null,
  };
}

export function normalizeConversation(value: unknown, id: string): VisitorConversation {
  const record = isRecord(value) ? value : {};
  const messages = Array.isArray(record.messages) ? record.messages.map(normalizeMessage).filter((m): m is VisitorMessage => m !== null) : [];
  return {
    id: typeof record.id === 'string' ? record.id : id,
    status: normalizeStatus(record.status) ?? 'ai',
    rating: typeof record.rating === 'number' ? record.rating : null,
    messages: messages.sort((a, b) => a.sequence - b.sequence),
  };
}

export function normalizeSession(value: unknown): VisitorSession | null {
  if (!isRecord(value) || typeof value.token !== 'string' || value.token === '') {
    return null;
  }
  return {
    token: value.token,
    visitorId: typeof value.visitorId === 'string' ? value.visitorId : '',
    conversationId: text(value.conversationId),
    expiresAt: text(value.expiresAt),
    name: text(value.name),
    email: text(value.email),
    rejectedAttributes: readRejections(value.rejectedAttributes),
  };
}

export function parseTurnEvent(type: string, data: unknown): TurnEvent | null {
  if (!isRecord(data)) {
    return null;
  }
  switch (type) {
    case 'conversation':
      return typeof data.conversationId === 'string' ? { type, conversationId: data.conversationId } : null;
    case 'tool-call':
      return { type, toolName: typeof data.toolName === 'string' ? data.toolName : '' };
    case 'tool-result':
      return { type, citations: normalizeCitations(data.citations) };
    case 'text-delta':
      return typeof data.text === 'string'
        ? { type, text: data.text, step: typeof data.step === 'number' && data.step > 0 ? data.step : null }
        : null;
    case 'step-finished':
      return typeof data.step === 'number' && (data.kind === 'interim' || data.kind === 'final') ? { type, step: data.step, kind: data.kind } : null;
    case 'error':
      return { type, message: text(data.message) ?? '', code: text(data.code) };
    case 'done':
      return { type, conversationId: typeof data.conversationId === 'string' ? data.conversationId : '' };
    case 'confirmation-required':
      return typeof data.callId === 'string'
        ? { type, callId: data.callId, title: text(data.title) ?? '', details: readDetails(data.details) }
        : null;
    case 'handover-offered':
      return typeof data.callId === 'string' ? { type, callId: data.callId } : null;
    default:
      return null;
  }
}

export function parseLiveEvent(event: string, data: unknown): LiveEvent | null {
  switch (event) {
    case 'message': {
      const message = normalizeMessage(data);
      return message ? { type: 'message', message } : null;
    }
    case 'turn': {
      const turn = isRecord(data) && typeof data.type === 'string' ? parseTurnEvent(data.type, data.data) : null;
      return turn ? { type: 'turn', event: turn } : null;
    }
    case 'conversation': {
      const status = isRecord(data) ? normalizeStatus(data.status) : null;
      return status ? { type: 'status', status } : null;
    }
    default:
      return null;
  }
}
