import { isAbortError, readSseStream } from '../../lib/sse';
import type { HostAttributeValue, HostPage, HostUser } from './host';
import {
  isRecord,
  normalizeConversation,
  normalizeMessage,
  normalizeSession,
  parseLiveEvent,
  parseTurnEvent,
  readRejections,
  type AttributeRejection,
  type LiveEvent,
  type TurnEvent,
  type VisitorConversation,
  type VisitorMessage,
  type VisitorSession,
} from './types';

export const CONVERSATION_BUSY = 'conversation_busy';
export const CONVERSATION_CLOSED = 'conversation_closed';
export const PERSON_JOINED = 'person_joined';
export const DAILY_CAP_REACHED = 'daily_cap_reached';

interface WidgetApiErrorInit {
  status: number;
  message: string;
  code?: string | null;
  fieldErrors?: Record<string, string[]>;
  retryAfterSeconds?: number | null;
  cause?: unknown;
}

// `status` is 0 when the API could not be reached.
export class WidgetApiError extends Error {
  readonly status: number;
  readonly code: string | null;
  readonly fieldErrors: Record<string, string[]>;
  readonly retryAfterSeconds: number | null;

  constructor(init: WidgetApiErrorInit) {
    super(init.message, init.cause === undefined ? undefined : { cause: init.cause });
    this.name = 'WidgetApiError';
    this.status = init.status;
    this.code = init.code ?? null;
    this.fieldErrors = init.fieldErrors ?? {};
    this.retryAfterSeconds = init.retryAfterSeconds ?? null;
  }

  errorFor(field: string): string | undefined {
    const wanted = field.toLowerCase();
    return Object.entries(this.fieldErrors).find(([key]) => key.replace(/^\$\.?/, '').toLowerCase() === wanted)?.[1][0];
  }
}

export function isApiError(error: unknown, status?: number): error is WidgetApiError {
  return error instanceof WidgetApiError && (status === undefined || error.status === status);
}

function parseRetryAfter(value: string | null): number | null {
  if (!value) {
    return null;
  }
  const seconds = Number(value);
  if (Number.isFinite(seconds)) {
    return Math.max(0, seconds);
  }
  const date = Date.parse(value);
  return Number.isNaN(date) ? null : Math.max(0, Math.round((date - Date.now()) / 1000));
}

async function toApiError(response: Response): Promise<WidgetApiError> {
  let problem: Record<string, unknown> = {};
  try {
    const parsed: unknown = await response.json();
    if (isRecord(parsed)) {
      problem = parsed;
    }
  } catch {
    // Not JSON, such as a proxy's error page.
  }
  const fieldErrors: Record<string, string[]> = {};
  if (isRecord(problem.errors)) {
    for (const [field, messages] of Object.entries(problem.errors)) {
      const list = (Array.isArray(messages) ? messages : [messages]).filter((m): m is string => typeof m === 'string' && m !== '');
      if (list.length > 0) {
        fieldErrors[field] = list;
      }
    }
  }
  const detail = typeof problem.detail === 'string' ? problem.detail : null;
  const title = typeof problem.title === 'string' ? problem.title : null;
  return new WidgetApiError({
    status: response.status,
    message: Object.values(fieldErrors)[0]?.[0] ?? detail ?? title ?? `Request failed with HTTP ${response.status}.`,
    code: typeof problem.code === 'string' ? problem.code : null,
    fieldErrors,
    retryAfterSeconds: parseRetryAfter(response.headers.get('Retry-After')),
  });
}

function networkError(cause: unknown): WidgetApiError {
  return new WidgetApiError({ status: 0, message: 'The chat service cannot be reached.', cause });
}

interface RequestOptions {
  method?: 'GET' | 'POST';
  token?: string | null;
  body?: unknown;
  signal?: AbortSignal;
}

export type SendOutcome =
  | { kind: 'stream'; events: AsyncGenerator<TurnEvent, void, undefined> }
  // 202: a person handles the conversation, so the message went to them and no answer streams.
  | { kind: 'relayed'; message: VisitorMessage | null };

export interface FollowOptions {
  after: number;
  // Reconnects send `after` as Last-Event-ID, as EventSource does.
  resume: boolean;
  signal: AbortSignal;
  onOpen?: () => void;
}

// Same-origin and relative, as the frame's Content-Security-Policy only allows 'self'.
export class WidgetApi {
  private readonly base: string;

  constructor(widgetKey: string) {
    this.base = `/api/public/widgets/${encodeURIComponent(widgetKey)}`;
  }

  private headers(method: string, token: string | null | undefined, extra: Record<string, string> = {}): Record<string, string> {
    const headers: Record<string, string> = { Accept: 'application/json', ...extra };
    if (method === 'POST') {
      headers['X-KA-Request'] = '1';
    }
    if (token) {
      headers.Authorization = `Bearer ${token}`;
    }
    return headers;
  }

  private async fetch(path: string, init: RequestInit): Promise<Response> {
    try {
      return await fetch(`${this.base}${path}`, init);
    } catch (error) {
      throw isAbortError(error) ? error : networkError(error);
    }
  }

  private async request<T>(path: string, { method = 'GET', token, body, signal }: RequestOptions = {}): Promise<T> {
    const extra: Record<string, string> = body === undefined ? {} : { 'Content-Type': 'application/json' };
    const response = await this.fetch(path, {
      method,
      headers: this.headers(method, token, extra),
      body: body === undefined ? null : JSON.stringify(body),
      signal: signal ?? null,
    });
    if (!response.ok) {
      throw await toApiError(response);
    }
    if (response.status === 204) {
      return undefined as T;
    }
    const raw = await response.text();
    try {
      return (raw.trim() ? JSON.parse(raw) : undefined) as T;
    } catch (error) {
      throw new WidgetApiError({ status: response.status, message: 'The chat service sent an invalid response.', cause: error });
    }
  }

  getConfig(signal?: AbortSignal): Promise<unknown> {
    return this.request<unknown>('/config', signal ? { signal } : {});
  }

  // user is the signed-in user of the host page; the API checks its hash and otherwise treats the visitor as anonymous.
  async startSession(
    token: string | null,
    user: HostUser | null = null,
    context: { attributes?: Record<string, HostAttributeValue>; page?: HostPage | null } = {},
  ): Promise<VisitorSession> {
    const body: { token?: string; user?: HostUser; attributes?: Record<string, HostAttributeValue>; page?: HostPage } = {};
    if (token) {
      body.token = token;
    }
    if (user) {
      body.user = user;
    }
    if (context.attributes && Object.keys(context.attributes).length > 0) {
      body.attributes = context.attributes;
    }
    if (context.page) {
      body.page = context.page;
    }
    const session = normalizeSession(await this.request<unknown>('/sessions', { method: 'POST', body }));
    if (!session) {
      throw new WidgetApiError({ status: 0, message: 'The chat service sent an invalid session.' });
    }
    return session;
  }

  // Returns the keys the server refused, with the reason.
  async updateAttributes(token: string, attributes: Record<string, HostAttributeValue>): Promise<AttributeRejection[]> {
    const result = await this.request<unknown>('/attributes', { method: 'POST', token, body: { attributes } });
    return readRejections(isRecord(result) ? result.rejected : null);
  }

  async getConversation(token: string, id: string): Promise<VisitorConversation> {
    return normalizeConversation(await this.request<unknown>(`/conversations/${encodeURIComponent(id)}`, { token }), id);
  }

  async saveContact(token: string, contact: { name?: string; email?: string }): Promise<{ name: string | null; email: string | null }> {
    const result = await this.request<unknown>('/contact', { method: 'POST', token, body: contact });
    const record = isRecord(result) ? result : {};
    return {
      name: typeof record.name === 'string' ? record.name : null,
      email: typeof record.email === 'string' ? record.email : null,
    };
  }

  async requestHandover(token: string, id: string, contact: { name?: string; email?: string }): Promise<{ status: string; started: boolean }> {
    const result = await this.request<unknown>(`/conversations/${encodeURIComponent(id)}/handover`, { method: 'POST', token, body: contact });
    const record = isRecord(result) ? result : {};
    return { status: typeof record.status === 'string' ? record.status : 'ai', started: record.started === true };
  }

  // 409 with PERSON_JOINED once someone from the team has joined.
  cancelHandover(token: string, id: string): Promise<void> {
    return this.request<void>(`/conversations/${encodeURIComponent(id)}/handover/cancel`, { method: 'POST', token });
  }

  closeConversation(token: string, id: string): Promise<void> {
    return this.request<void>(`/conversations/${encodeURIComponent(id)}/close`, { method: 'POST', token });
  }

  sendFeedback(token: string, id: string, feedback: { rating: number; messageId?: string; comment?: string }): Promise<unknown> {
    return this.request<unknown>(`/conversations/${encodeURIComponent(id)}/feedback`, { method: 'POST', token, body: feedback });
  }

  async sendMessage(
    token: string,
    body: { message: string; conversationId?: string; confirmation?: { callId: string; approved: boolean } },
    signal: AbortSignal,
  ): Promise<SendOutcome> {
    const response = await this.fetch('/messages', {
      method: 'POST',
      headers: this.headers('POST', token, { Accept: 'text/event-stream, application/json', 'Content-Type': 'application/json' }),
      body: JSON.stringify(body),
      signal,
      cache: 'no-store',
    });
    if (!response.ok) {
      throw await toApiError(response);
    }
    const contentType = response.headers.get('Content-Type') ?? '';
    if (response.status === 202 || !contentType.includes('text/event-stream')) {
      let payload: unknown = null;
      try {
        payload = await response.json();
      } catch {
        // An empty 202 still means the message was accepted.
      }
      return { kind: 'relayed', message: normalizeMessage(payload) };
    }
    if (!response.body) {
      throw networkError(new Error('The answer stream is empty.'));
    }
    return { kind: 'stream', events: turnEvents(response.body, signal) };
  }

  async *followConversation(token: string, id: string, { after, resume, signal, onOpen }: FollowOptions): AsyncGenerator<LiveEvent, void, undefined> {
    const extra: Record<string, string> = { Accept: 'text/event-stream' };
    let path = `/conversations/${encodeURIComponent(id)}/events`;
    if (resume) {
      extra['Last-Event-ID'] = String(after);
    } else {
      path += `?after=${after}`;
    }
    const response = await this.fetch(path, { method: 'GET', headers: this.headers('GET', token, extra), signal, cache: 'no-store' });
    if (!response.ok) {
      throw await toApiError(response);
    }
    if (!response.body) {
      throw networkError(new Error('The event stream is empty.'));
    }
    onOpen?.();
    for await (const message of readSseStream(response.body, signal)) {
      const event = parseLiveEvent(message.event, parseJson(message.data));
      if (event) {
        yield event;
      }
    }
  }
}

function parseJson(data: string): unknown {
  try {
    return JSON.parse(data);
  } catch {
    return undefined;
  }
}

async function* turnEvents(body: ReadableStream<Uint8Array>, signal: AbortSignal): AsyncGenerator<TurnEvent, void, undefined> {
  for await (const message of readSseStream(body, signal)) {
    const event = parseTurnEvent(message.event, parseJson(message.data));
    if (event) {
      yield event;
    }
  }
}

export interface PreviewSession {
  session: VisitorSession;
  config: unknown;
}

// "Test this widget": an admin's visitor session from the portal API. It needs the admin's session cookie,
// which is sent because the preview is served from the portal's origin.
export async function fetchPreviewSession(widgetId: string, signal?: AbortSignal): Promise<PreviewSession> {
  let response: Response;
  try {
    response = await fetch(`/api/widgets/${encodeURIComponent(widgetId)}/preview-session`, {
      method: 'POST',
      credentials: 'same-origin',
      headers: { Accept: 'application/json', 'X-KA-Request': '1' },
      signal: signal ?? null,
    });
  } catch (error) {
    throw isAbortError(error) ? error : networkError(error);
  }
  if (!response.ok) {
    throw await toApiError(response);
  }
  const body: unknown = await response.json().catch(() => null);
  const session = isRecord(body) ? normalizeSession(body.session) : null;
  if (!session || !isRecord(body) || !isRecord(body.config)) {
    throw new WidgetApiError({ status: response.status, message: 'The preview session is not valid.' });
  }
  return { session, config: body.config };
}
