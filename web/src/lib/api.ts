import { checkSession, REQUEST_HEADERS } from './session';
import { isAbortError } from './sse';
import type { ProblemDetails } from './types';

const API_BASE = (import.meta.env.VITE_API_BASE_URL ?? '').replace(/\/+$/, '');

export interface ApiErrorInit {
  status: number;
  message: string;
  title?: string | null;
  detail?: string | null;
  fieldErrors?: Record<string, string[]>;
  problem?: ProblemDetails | null;
  cause?: unknown;
}

/** An HTTP or network failure. `status` is 0 when the API could not be reached at all. */
export class ApiError extends Error {
  readonly status: number;
  readonly title: string | null;
  readonly detail: string | null;
  readonly fieldErrors: Record<string, string[]>;
  readonly problem: ProblemDetails | null;

  constructor(init: ApiErrorInit) {
    super(init.message, init.cause === undefined ? undefined : { cause: init.cause });
    this.name = 'ApiError';
    this.status = init.status;
    this.title = init.title ?? null;
    this.detail = init.detail ?? null;
    this.fieldErrors = init.fieldErrors ?? {};
    this.problem = init.problem ?? null;
  }

  get isValidation(): boolean {
    return Object.keys(this.fieldErrors).length > 0;
  }

  get isNetworkError(): boolean {
    return this.status === 0;
  }

  /** Matches the field case-insensitively, ignoring a `$.` or `request.` prefix. */
  errorsFor(field: string): string[] {
    const wanted = normalizeFieldName(field);
    return Object.entries(this.fieldErrors)
      .filter(([key]) => normalizeFieldName(key) === wanted)
      .flatMap(([, messages]) => messages);
  }

  /** Messages for all other fields, e.g. the ones a form has no input for. */
  errorsExcept(fields: readonly string[]): string[] {
    const known = new Set(fields.map(normalizeFieldName));
    return Object.entries(this.fieldErrors)
      .filter(([key]) => !known.has(normalizeFieldName(key)))
      .flatMap(([, messages]) => messages);
  }
}

function normalizeFieldName(field: string): string {
  return field
    .replace(/^\$\.?/, '')
    .replace(/^request\./i, '')
    .toLowerCase();
}

export function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function readFieldErrors(value: unknown): Record<string, string[]> {
  if (!isRecord(value)) {
    return {};
  }
  const result: Record<string, string[]> = {};
  for (const [field, messages] of Object.entries(value)) {
    const list = Array.isArray(messages) ? messages : [messages];
    const texts = list.filter((m): m is string => typeof m === 'string' && m.trim() !== '');
    if (texts.length > 0) {
      result[field] = texts;
    }
  }
  return result;
}

/** "SourceIds[0]" → "Source ids [0]", "$.url" → "Url". */
function humanizeField(field: string): string {
  const name = field.replace(/^\$\.?/, '').replace(/^request\./i, '');
  if (!name) {
    return '';
  }
  const spaced = name.replace(/([a-z0-9])([A-Z])/g, '$1 $2').replace(/[_.]/g, ' ');
  return spaced.charAt(0).toUpperCase() + spaced.slice(1).toLowerCase();
}

function flattenFieldErrors(fieldErrors: Record<string, string[]>): string {
  return Object.entries(fieldErrors)
    .flatMap(([field, messages]) => {
      const label = humanizeField(field);
      return messages.map((message) =>
        label && !message.toLowerCase().includes(label.toLowerCase()) ? `${label}: ${message}` : message,
      );
    })
    .join(' ');
}

// The API's generic titles ("Unauthorized", "Forbidden") don't say what to do.
const ACCESS_MESSAGES: Readonly<Record<number, string>> = {
  401: 'Your session has ended. Sign in again.',
  403: 'You need the admin role to do this.',
};

function statusFallback(status: number, statusText: string): string {
  if (status === 502 || status === 504) {
    return `The API is not reachable (HTTP ${status}). Check that the backend is running.`;
  }
  return `Request failed with HTTP ${status}${statusText ? ` ${statusText}` : ''}.`;
}

export async function toApiError(response: Response): Promise<ApiError> {
  let text = '';
  try {
    text = await response.text();
  } catch {
    // Unreadable body: fall back to the status line.
  }

  let problem: ProblemDetails | null = null;
  if (text) {
    try {
      const parsed: unknown = JSON.parse(text);
      if (isRecord(parsed)) {
        problem = parsed as ProblemDetails;
      }
    } catch {
      // Not JSON, e.g. an HTML error page from a proxy.
    }
  }

  const fieldErrors = readFieldErrors(problem?.errors);
  const title = typeof problem?.title === 'string' && problem.title.trim() ? problem.title.trim() : null;
  const detail = typeof problem?.detail === 'string' && problem.detail.trim() ? problem.detail.trim() : null;
  const accessMessage = ACCESS_MESSAGES[response.status];

  let message: string;
  if (Object.keys(fieldErrors).length > 0) {
    message = flattenFieldErrors(fieldErrors);
  } else if (detail) {
    message = detail;
  } else if (accessMessage) {
    message = accessMessage;
  } else if (title) {
    message = title;
  } else if (!problem && text && text.length <= 300 && !/^\s*</.test(text)) {
    message = text.trim();
  } else {
    message = statusFallback(response.status, response.statusText);
  }

  return new ApiError({ status: response.status, message, title, detail, fieldErrors, problem });
}

export function errorMessage(error: unknown): string {
  if (error instanceof Error) {
    return error.message;
  }
  if (typeof error === 'string') {
    return error;
  }
  return 'Something went wrong.';
}

export function networkError(cause: unknown): ApiError {
  return new ApiError({ status: 0, message: 'Cannot reach the API. Check that the backend is running.', cause });
}

export type QueryValue = string | number | boolean | null | undefined;

interface RequestOptions {
  method?: string;
  body?: unknown;
  /** Sent as multipart instead of `body`. */
  formData?: FormData;
  query?: Record<string, QueryValue>;
  signal?: AbortSignal;
}

export function buildUrl(path: string, query?: Record<string, QueryValue>): string {
  let url = `${API_BASE}${path}`;
  if (query) {
    const params = new URLSearchParams();
    for (const [key, value] of Object.entries(query)) {
      if (value !== undefined && value !== null && value !== '') {
        params.set(key, String(value));
      }
    }
    // Commas need no escaping in a query string; keeping them makes lists (status, ids) readable.
    const qs = params.toString().replace(/%2C/gi, ',');
    if (qs) {
      url += `?${qs}`;
    }
  }
  return url;
}

export async function request<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const headers: Record<string, string> = { Accept: 'application/json', ...REQUEST_HEADERS };
  let body: BodyInit | undefined;
  if (options.formData) {
    body = options.formData;
  } else if (options.body !== undefined) {
    headers['Content-Type'] = 'application/json';
    body = JSON.stringify(options.body);
  }

  let response: Response;
  try {
    response = await fetch(buildUrl(path, options.query), {
      method: options.method ?? (body === undefined ? 'GET' : 'POST'),
      headers,
      body: body ?? null,
      signal: options.signal ?? null,
    });
  } catch (error) {
    if (isAbortError(error)) {
      throw error;
    }
    throw networkError(error);
  }

  if (!response.ok) {
    checkSession(response);
    throw await toApiError(response);
  }
  if (response.status === 204 || response.status === 205) {
    return undefined as T;
  }
  const text = await response.text();
  if (!text.trim()) {
    return undefined as T;
  }
  try {
    return JSON.parse(text) as T;
  } catch (error) {
    throw new ApiError({ status: response.status, message: 'The API returned a response that is not valid JSON.', cause: error });
  }
}

/** Keeps only the strings of a JSON array; anything else gives []. */
export function asStrings(value: unknown): string[] {
  return Array.isArray(value) ? value.filter((item): item is string => typeof item === 'string') : [];
}

/** Accepts a bare JSON array or a wrapper such as `{ items: [] }`. */
export function asArray<T>(value: unknown): T[] {
  if (Array.isArray(value)) {
    return value as T[];
  }
  if (isRecord(value)) {
    for (const key of ['items', 'hits', 'results', 'data', 'value']) {
      const candidate = value[key];
      if (Array.isArray(candidate)) {
        return candidate as T[];
      }
    }
  }
  return [];
}
