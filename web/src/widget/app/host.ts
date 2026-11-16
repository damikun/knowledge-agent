import { envelope, parseEditorMessage, type EditorMessage, type PreviewMessage } from '../shared/preview';
import { isRecord } from './types';

const HOST_SOURCE = 'knowledge-agent';

export type FrameMessage =
  | { type: 'ready' }
  | { type: 'close' }
  | { type: 'unread'; count: number }
  | { type: 'busy'; busy: boolean }
  | { type: 'failed' }
  | { type: 'unavailable' };

export function postToHost(message: FrameMessage): void {
  try {
    window.parent.postMessage({ source: HOST_SOURCE, ...message }, '*');
  } catch {}
}

export type HostCommand = 'open' | 'close';

export interface HostChannel {
  post(message: FrameMessage): void;
  listen(listener: (command: HostCommand) => void): () => void;
}

export const parentHost: HostChannel = {
  post: postToHost,
  listen(listener) {
    const onMessage = (event: MessageEvent) => {
      const command = readHostCommand(event);
      if (command) {
        listener(command);
      }
    };
    window.addEventListener('message', onMessage);
    return () => window.removeEventListener('message', onMessage);
  },
};

export const silentHost: HostChannel = {
  post: () => undefined,
  listen: () => () => undefined,
};

export function readHostCommand(event: MessageEvent): HostCommand | null {
  if (event.source !== window.parent || !isRecord(event.data) || event.data.source !== HOST_SOURCE) {
    return null;
  }
  return event.data.type === 'open' || event.data.type === 'close' ? event.data.type : null;
}

export interface HostUser {
  userId: string;
  userHash: string;
  name?: string;
  email?: string;
  attributes?: Record<string, HostAttributeValue>;
}

export type HostAttributeValue = string | number | boolean | null;

export interface HostPage {
  url: string;
  referrer: string | null;
  language: string | null;
}

export type HostIdentityCommand =
  { type: 'identity'; user: HostUser | null } | { type: 'logout' } | { type: 'attributes'; attributes: Record<string, HostAttributeValue> };

function readAttributes(value: unknown): Record<string, HostAttributeValue> {
  const attributes: Record<string, HostAttributeValue> = {};
  if (isRecord(value)) {
    for (const [key, item] of Object.entries(value)) {
      if (item === null || typeof item === 'string' || typeof item === 'number' || typeof item === 'boolean') {
        attributes[key] = item;
      }
    }
  }
  return attributes;
}

function readPage(value: unknown): HostPage | null {
  if (!isRecord(value) || typeof value.url !== 'string') {
    return null;
  }
  return {
    url: value.url,
    referrer: typeof value.referrer === 'string' ? value.referrer : null,
    language: typeof value.language === 'string' ? value.language : null,
  };
}

function readHostUser(value: unknown): HostUser | null {
  if (!isRecord(value) || typeof value.userId !== 'string' || typeof value.userHash !== 'string') {
    return null;
  }
  const user: HostUser = { userId: value.userId, userHash: value.userHash };
  if (typeof value.name === 'string') {
    user.name = value.name;
  }
  if (typeof value.email === 'string') {
    user.email = value.email;
  }
  if (isRecord(value.attributes)) {
    user.attributes = readAttributes(value.attributes);
  }
  return user;
}

function readHostIdentity(event: MessageEvent): HostIdentityCommand | null {
  if (event.source !== window.parent || !isRecord(event.data) || event.data.source !== HOST_SOURCE) {
    return null;
  }
  if (event.data.type === 'logout') {
    return { type: 'logout' };
  }
  if (event.data.type === 'attributes') {
    return { type: 'attributes', attributes: readAttributes(event.data.attributes) };
  }
  return event.data.type === 'identity' ? { type: 'identity', user: readHostUser(event.data.user) } : null;
}

export interface HostIdentity {
  readonly ready: Promise<void>;
  current: () => HostUser | null;
  attributes: () => Record<string, HostAttributeValue>;
  page: () => HostPage | null;
  subscribe: (listener: (command: HostIdentityCommand) => void) => () => void;
}

export function listenForHostIdentity(waitMs = 1500): HostIdentity {
  let user: HostUser | null = null;
  let attributes: Record<string, HostAttributeValue> = {};
  let page: HostPage | null = null;
  let settle: () => void = () => undefined;
  const ready = new Promise<void>((resolve) => {
    settle = resolve;
  });
  const timer = window.setTimeout(() => settle(), waitMs);
  const listeners = new Set<(command: HostIdentityCommand) => void>();
  window.addEventListener('message', (event) => {
    const command = readHostIdentity(event);
    if (!command) {
      return;
    }
    if (command.type === 'attributes') {
      attributes = { ...attributes, ...command.attributes };
    } else {
      user = command.type === 'identity' ? command.user : null;
      if (command.type === 'identity' && isRecord(event.data)) {
        attributes = readAttributes(event.data.attributes);
        page = readPage(event.data.page) ?? page;
      } else {
        attributes = {};
      }
      window.clearTimeout(timer);
      settle();
    }
    for (const listener of [...listeners]) {
      listener(command);
    }
  });
  return {
    ready,
    current: () => user,
    attributes: () => attributes,
    page: () => page,
    subscribe: (listener) => {
      listeners.add(listener);
      return () => {
        listeners.delete(listener);
      };
    },
  };
}

export function readEditorMessage(event: MessageEvent): EditorMessage | null {
  if (event.origin !== window.location.origin || event.source !== window.parent) {
    return null;
  }
  return parseEditorMessage(event.data);
}

export function postToEditor(message: PreviewMessage): void {
  window.parent.postMessage(envelope(message), window.location.origin);
}

export function readFrameLanguage(location: Pick<Location, 'search'>): string {
  const language = new URLSearchParams(location.search).get('lang')?.trim() ?? '';
  return /^[a-z]{2,3}(-[a-z0-9]{1,8})*$/i.test(language) ? language : 'en';
}

export function readFrameLocation(location: Pick<Location, 'pathname'>): { widgetKey: string; preview: boolean } {
  if (/\/preview\/?$/.test(location.pathname)) {
    return { widgetKey: '', preview: true };
  }
  const match = /\/frame\/([^/]+)\/?$/.exec(location.pathname);
  let widgetKey = '';
  if (match) {
    try {
      widgetKey = decodeURIComponent(match[1]!);
    } catch {
      widgetKey = '';
    }
  }
  return { widgetKey, preview: false };
}
