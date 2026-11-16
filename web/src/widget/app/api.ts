import { pathOf, type Endpoint, type Query } from '../../lib/endpoint';
import { isAbortError, readSseStream } from '../../lib/sse';
import { widgetEndpoints } from '../shared/endpoints';
import type { HostAttributeValue, HostPage, HostUser } from './host';
import {
  isRecord,
  normalizeConversation,
  normalizeMessage,
  normalizeSession,
  normalizeSummaries,
  parseLiveEvent,
  parseTurnEvent,
  readRejections,
  type AttributeRejection,
  type ConversationSummary,
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
  } catch {}
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

export interface Contact {
  name?: string;
  email?: string;
}

export interface Feedback {
  rating: number;
  messageId?: string;
  comment?: string;
}

export interface VisitorAuth {
  readonly signal: AbortSignal;
  token(): Promise<string>;
  renew(): Promise<string>;
}

export type SendOutcome =
  { kind: 'stream'; events: AsyncGenerator<TurnEvent, void, undefined> } | { kind: 'relayed'; message: VisitorMessage | null };

export interface FollowOptions {
  after: number;
  resume: boolean;
  signal: AbortSignal;
  onOpen?: () => void;
}

interface Call {
  id?: string;
  query?: Query;
  body?: unknown;
  accept?: string;
  headers?: Record<string, string>;
  signal?: AbortSignal | undefined;
}

type WidgetEndpoint = Endpoint<`${string}{key}${string}`>;

export class WidgetApi {
  constructor(
    private readonly key: string,
    private readonly visitor: () => VisitorAuth,
  ) {}

  async getConfig(signal?: AbortSignal): Promise<unknown> {
    return readJson(await this.anonymous(widgetEndpoints.config, { signal }));
  }

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
    const session = normalizeSession(await readJson(await this.anonymous(widgetEndpoints.startSession, { body })));
    if (!session) {
      throw new WidgetApiError({ status: 0, message: 'The chat service sent an invalid session.' });
    }
    return session;
  }

  async updateAttributes(attributes: Record<string, HostAttributeValue>): Promise<AttributeRejection[]> {
    const result = await this.json(widgetEndpoints.updateAttributes, { body: { attributes } });
    return readRejections(isRecord(result) ? result.rejected : null);
  }

  async saveContact(contact: Contact): Promise<{ name: string | null; email: string | null }> {
    const result = await this.json(widgetEndpoints.saveContact, { body: contact });
    const record = isRecord(result) ? result : {};
    return {
      name: typeof record.name === 'string' ? record.name : null,
      email: typeof record.email === 'string' ? record.email : null,
    };
  }

  async listConversations(signal?: AbortSignal): Promise<ConversationSummary[]> {
    return normalizeSummaries(await this.json(widgetEndpoints.listConversations, { signal }));
  }

  async getConversation(id: string, signal?: AbortSignal): Promise<VisitorConversation> {
    return normalizeConversation(await this.json(widgetEndpoints.getConversation, { id, signal }), id);
  }

  async closeConversation(id: string, signal?: AbortSignal): Promise<void> {
    await this.json(widgetEndpoints.closeConversation, { id, signal });
  }

  async requestHandover(id: string, contact: Contact, signal?: AbortSignal): Promise<{ status: string; started: boolean }> {
    const result = await this.json(widgetEndpoints.requestHandover, { id, body: contact, signal });
    const record = isRecord(result) ? result : {};
    return { status: typeof record.status === 'string' ? record.status : 'ai', started: record.started === true };
  }

  async cancelHandover(id: string, signal?: AbortSignal): Promise<void> {
    await this.json(widgetEndpoints.cancelHandover, { id, signal });
  }

  async sendFeedback(id: string, feedback: Feedback, signal?: AbortSignal): Promise<void> {
    await this.json(widgetEndpoints.sendFeedback, { id, body: feedback, signal });
  }

  async sendMessage(
    body: { message: string; conversationId?: string; confirmation?: { callId: string; approved: boolean } },
    signal: AbortSignal,
  ): Promise<SendOutcome> {
    const scope = this.scope(signal);
    const response = await this.authorized(widgetEndpoints.sendMessage, { body, accept: 'text/event-stream, application/json' }, scope);
    if (response.status === 202 || !(response.headers.get('Content-Type') ?? '').includes('text/event-stream')) {
      const payload: unknown = await response.json().catch(() => null);
      return { kind: 'relayed', message: normalizeMessage(payload) };
    }
    return { kind: 'stream', events: turnEvents(streamOf(response), scope) };
  }

  async *followConversation(id: string, { after, resume, signal, onOpen }: FollowOptions): AsyncGenerator<LiveEvent, void, undefined> {
    const scope = this.scope(signal);
    const call: Call = resume ? { id, headers: { 'Last-Event-ID': String(after) } } : { id, query: { after } };
    const body = streamOf(await this.authorized(widgetEndpoints.followConversation, { ...call, accept: 'text/event-stream' }, scope));
    onOpen?.();
    for await (const message of readSseStream(body, scope)) {
      const event = parseLiveEvent(message.event, parseJson(message.data));
      if (event) {
        yield event;
      }
    }
  }

  private async json(target: WidgetEndpoint, call: Call): Promise<unknown> {
    const scope = this.scope(call.signal);
    const value = await readJson(await this.authorized(target, call, scope));
    scope.throwIfAborted();
    return value;
  }

  private scope(signal: AbortSignal | undefined): AbortSignal {
    const visitor = this.visitor().signal;
    return signal ? AbortSignal.any([visitor, signal]) : visitor;
  }

  private async authorized(target: WidgetEndpoint, call: Call, scope: AbortSignal): Promise<Response> {
    const visitor = this.visitor();
    let response = await this.send(target, call, scope, await visitor.token());
    if (response.status === 401) {
      response = await this.send(target, call, scope, await visitor.renew());
    }
    return ok(response);
  }

  private async anonymous(target: WidgetEndpoint, call: Call): Promise<Response> {
    return ok(await this.send(target, call, call.signal, null));
  }

  private async send(target: WidgetEndpoint, call: Call, signal: AbortSignal | undefined, token: string | null): Promise<Response> {
    signal?.throwIfAborted();
    const headers: Record<string, string> = { Accept: call.accept ?? 'application/json', ...call.headers };
    if (call.body !== undefined) {
      headers['Content-Type'] = 'application/json';
    }
    if (target.method === 'POST') {
      headers['X-KA-Request'] = '1';
    }
    if (token) {
      headers.Authorization = `Bearer ${token}`;
    }
    const url = pathOf(target as Endpoint, { key: this.key, ...(call.id === undefined ? {} : { id: call.id }) }, call.query);
    try {
      return await fetch(url, {
        method: target.method,
        headers,
        body: call.body === undefined ? null : JSON.stringify(call.body),
        signal: signal ?? null,
        cache: 'no-store',
      });
    } catch (error) {
      throw isAbortError(error) ? error : networkError(error);
    }
  }
}

async function ok(response: Response): Promise<Response> {
  if (!response.ok) {
    throw await toApiError(response);
  }
  return response;
}

async function readJson(response: Response): Promise<unknown> {
  if (response.status === 204) {
    return undefined;
  }
  const raw = await response.text();
  try {
    return raw.trim() ? JSON.parse(raw) : undefined;
  } catch (error) {
    throw new WidgetApiError({ status: response.status, message: 'The chat service sent an invalid response.', cause: error });
  }
}

function streamOf(response: Response): ReadableStream<Uint8Array> {
  if (!response.body) {
    throw networkError(new Error('The stream is empty.'));
  }
  return response.body;
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

export async function fetchPreviewSession(widgetId: string, signal?: AbortSignal): Promise<PreviewSession> {
  let response: Response;
  try {
    const target = widgetEndpoints.previewSession;
    response = await fetch(pathOf(target, { id: widgetId }), {
      method: target.method,
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
