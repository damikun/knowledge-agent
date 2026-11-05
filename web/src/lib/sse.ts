// EventSource only does GET, and POST /api/chat streams text/event-stream, so the stream is parsed here (WHATWG rules,
// plus a final event without a trailing blank line).

import { checkSession, REQUEST_HEADERS } from './session';

export interface SseMessage {
  /** The `event:` field; "message" when absent. */
  event: string;
  /** All `data:` lines of the event joined with "\n". */
  data: string;
  id?: string;
}

/** Feed decoded text with push() and call flush() when the stream ends. */
export class SseParser {
  private buffer = '';
  private eventName = '';
  private dataLines: string[] = [];
  private lastEventId: string | undefined;
  private skipLeadingLineFeed = false;
  private started = false;

  push(chunk: string): SseMessage[] {
    let text = chunk;
    if (!this.started && text.length > 0) {
      this.started = true;
      if (text.charCodeAt(0) === 0xfeff) {
        text = text.slice(1);
      }
    }
    if (this.skipLeadingLineFeed && text.length > 0) {
      // The previous chunk ended with "\r"; a "\n" here belongs to the same CRLF line break.
      if (text.startsWith('\n')) {
        text = text.slice(1);
      }
      this.skipLeadingLineFeed = false;
    }

    const events: SseMessage[] = [];
    const buffer = this.buffer + text;
    let lineStart = 0;
    for (let i = 0; i < buffer.length; i++) {
      const ch = buffer.charCodeAt(i);
      if (ch !== 0x0a && ch !== 0x0d) {
        continue;
      }
      const line = buffer.slice(lineStart, i);
      if (ch === 0x0d) {
        if (i + 1 < buffer.length) {
          if (buffer.charCodeAt(i + 1) === 0x0a) {
            i++;
          }
        } else {
          this.skipLeadingLineFeed = true;
        }
      }
      this.processLine(line, events);
      lineStart = i + 1;
    }
    this.buffer = buffer.slice(lineStart);
    return events;
  }

  /** Processes an unterminated last line and dispatches a pending event. */
  flush(): SseMessage[] {
    const events: SseMessage[] = [];
    if (this.buffer.length > 0) {
      const line = this.buffer;
      this.buffer = '';
      this.processLine(line, events);
    }
    this.dispatch(events);
    this.skipLeadingLineFeed = false;
    return events;
  }

  private processLine(line: string, events: SseMessage[]): void {
    if (line.length === 0) {
      this.dispatch(events);
      return;
    }
    if (line.startsWith(':')) {
      return; // comment / keep-alive
    }
    const colon = line.indexOf(':');
    const field = colon === -1 ? line : line.slice(0, colon);
    let value = colon === -1 ? '' : line.slice(colon + 1);
    if (value.startsWith(' ')) {
      value = value.slice(1);
    }
    switch (field) {
      case 'event':
        this.eventName = value;
        break;
      case 'data':
        this.dataLines.push(value);
        break;
      case 'id':
        if (!value.includes('\u0000')) {
          this.lastEventId = value;
        }
        break;
      default:
        // `retry` and unknown fields are irrelevant for a single fetch-based stream.
        break;
    }
  }

  private dispatch(events: SseMessage[]): void {
    if (this.dataLines.length === 0) {
      this.eventName = '';
      return;
    }
    const message: SseMessage = { event: this.eventName || 'message', data: this.dataLines.join('\n') };
    if (this.lastEventId !== undefined) {
      message.id = this.lastEventId;
    }
    events.push(message);
    this.eventName = '';
    this.dataLines = [];
  }
}

function abortReason(signal: AbortSignal): unknown {
  return signal.reason ?? new DOMException('The operation was aborted.', 'AbortError');
}

/** Checks the name rather than instanceof, so aborts from another realm match too. */
export function isAbortError(error: unknown): boolean {
  return typeof error === 'object' && error !== null && 'name' in error && (error as { name: unknown }).name === 'AbortError';
}

/** Aborting `signal` cancels the reader and makes the generator throw the abort reason. */
export async function* readSseStream(
  body: ReadableStream<Uint8Array>,
  signal?: AbortSignal,
): AsyncGenerator<SseMessage, void, undefined> {
  if (signal?.aborted) {
    throw abortReason(signal);
  }
  const reader = body.getReader();
  const decoder = new TextDecoder('utf-8');
  const parser = new SseParser();
  const onAbort = () => {
    reader.cancel(abortReason(signal!)).catch(() => undefined);
  };
  signal?.addEventListener('abort', onAbort, { once: true });
  try {
    for (;;) {
      const { done, value } = await reader.read();
      if (signal?.aborted) {
        throw abortReason(signal);
      }
      if (done) {
        break;
      }
      yield* parser.push(decoder.decode(value, { stream: true }));
    }
    yield* parser.push(decoder.decode());
    yield* parser.flush();
  } finally {
    signal?.removeEventListener('abort', onAbort);
    try {
      reader.releaseLock();
    } catch {
      // Already released or a read is still pending after cancellation.
    }
  }
}

export interface SseRequestInit {
  /** Defaults to POST when a body is given, GET otherwise. */
  method?: string;
  headers?: Record<string, string>;
  body?: BodyInit | null;
  signal?: AbortSignal;
  onErrorResponse?: (response: Response) => Promise<Error> | Error;
  /** Called once the stream is open, before its first event; a stream may stay quiet for a while. */
  onOpen?: (response: Response) => void;
}

export async function* sseRequest(url: string, init: SseRequestInit = {}): AsyncGenerator<SseMessage, void, undefined> {
  const response = await fetch(url, {
    method: init.method ?? (init.body != null ? 'POST' : 'GET'),
    headers: { Accept: 'text/event-stream', ...init.headers, ...REQUEST_HEADERS },
    body: init.body ?? null,
    signal: init.signal ?? null,
    cache: 'no-store',
  });
  if (!response.ok) {
    checkSession(response);
    throw init.onErrorResponse
      ? await init.onErrorResponse(response)
      : new Error(`Request failed with status ${response.status} ${response.statusText}`.trim());
  }
  if (!response.body) {
    throw new Error('The server returned an empty event stream.');
  }
  init.onOpen?.(response);
  yield* readSseStream(response.body, init.signal);
}
