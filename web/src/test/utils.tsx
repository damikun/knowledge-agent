import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, type RenderResult } from '@testing-library/react';
import type { ReactElement, ReactNode } from 'react';
import { createMemoryRouter, MemoryRouter, Route, RouterProvider, Routes, type RouteObject } from 'react-router';
import { vi, type Mock } from 'vitest';
import { ApiError } from '../lib/api';
import { adminUser } from './apiFixtures';

export function createTestQueryClient(): QueryClient {
  return new QueryClient({
    defaultOptions: {
      queries: { retry: false, gcTime: Infinity, refetchOnWindowFocus: false },
      mutations: { retry: false },
    },
  });
}

export interface RenderOptions {
  /** Initial URL. */
  route?: string;
  /** Route pattern the element is mounted at. */
  path?: string;
  queryClient?: QueryClient;
  /** Extra routes (e.g. targets of navigation). */
  extraRoutes?: ReactNode;
}

/** `rerender` keeps the providers. */
export function renderWithProviders(ui: ReactElement, options: RenderOptions = {}): RenderResult & { queryClient: QueryClient } {
  const queryClient = options.queryClient ?? createTestQueryClient();
  const Wrapper = ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={[options.route ?? '/']}>
        <Routes>
          <Route path={options.path ?? '*'} element={children} />
          {options.extraRoutes}
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>
  );
  const result = render(ui, { wrapper: Wrapper });
  return { ...result, queryClient };
}

/** Renders route objects in a data router as the app does, so lazy routes and route guards work. */
export function renderRouter(
  routes: RouteObject[],
  options: Pick<RenderOptions, 'route' | 'queryClient'> = {},
): RenderResult & { queryClient: QueryClient } {
  const queryClient = options.queryClient ?? createTestQueryClient();
  const router = createMemoryRouter(routes, { initialEntries: [options.route ?? '/'] });
  const result = render(
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={router} />
    </QueryClientProvider>,
  );
  return { ...result, queryClient };
}

export function jsonResponse(body: unknown, status = 200, contentType = 'application/json'): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': contentType } });
}

export function problemResponse(problem: Record<string, unknown>, status: number): Response {
  return jsonResponse({ status, ...problem }, status, 'application/problem+json');
}

/** The 401 of GET /api/auth/me when nobody is signed in. */
export function signedOutResponse(): Response {
  return problemResponse({ title: 'Not signed in', detail: 'Sign in to use the portal.' }, 401);
}

const encoder = new TextEncoder();

function sseBody(events: Array<{ event: string; data: unknown }>): string {
  return events.map(({ event, data }) => `event: ${event}\ndata: ${JSON.stringify(data)}\n\n`).join('');
}

/** A ReadableStream that emits `text` in chunks of `chunkSize` bytes (to exercise chunk boundaries). */
export function streamFromText(text: string, chunkSize = 7): ReadableStream<Uint8Array> {
  const bytes = encoder.encode(text);
  let offset = 0;
  return new ReadableStream<Uint8Array>({
    pull(controller) {
      if (offset >= bytes.length) {
        controller.close();
        return;
      }
      controller.enqueue(bytes.slice(offset, offset + chunkSize));
      offset += chunkSize;
    },
  });
}

export function sseResponse(events: Array<{ event: string; data: unknown }>, chunkSize = 23): Response {
  return new Response(streamFromText(sseBody(events), chunkSize), {
    status: 200,
    headers: { 'Content-Type': 'text/event-stream' },
  });
}

export interface ControlledEventStream {
  /** Answers a request with a new open stream, which the methods below then write to. */
  respond: (init?: RequestInit) => Response;
  send: (event: string, data: unknown, id?: number) => void;
  /** The server ends the stream. */
  close: () => void;
  /** The connection drops. */
  fail: () => void;
  /** Streams answered so far. */
  readonly opened: number;
  /** Whether the latest stream is still open: not closed, failed, cancelled or aborted by the client. */
  readonly isOpen: boolean;
}

/** An event stream the test writes to, such as GET /api/conversations/{id}/events. */
export function controlledEventStream(): ControlledEventStream {
  let controller: ReadableStreamDefaultController<Uint8Array> | null = null;
  let opened = 0;
  return {
    respond(init) {
      opened += 1;
      let own: ReadableStreamDefaultController<Uint8Array> | null = null;
      const stream = new ReadableStream<Uint8Array>({
        start(c) {
          controller = c;
          own = c;
        },
        cancel() {
          if (controller === own) {
            controller = null;
          }
        },
      });
      // The fetch mock does not abort; the client's signal stands for the closed connection.
      init?.signal?.addEventListener('abort', () => {
        if (controller === own) {
          controller = null;
        }
      });
      return new Response(stream, { headers: { 'Content-Type': 'text/event-stream' } });
    },
    send(event, data, id) {
      controller?.enqueue(encoder.encode(`${id === undefined ? '' : `id: ${id}\n`}event: ${event}\ndata: ${JSON.stringify(data)}\n\n`));
    },
    close() {
      controller?.close();
      controller = null;
    },
    fail() {
      controller?.error(new TypeError('network error'));
      controller = null;
    },
    get opened() {
      return opened;
    },
    get isOpen() {
      return controller !== null;
    },
  };
}

export type FetchHandler = (url: URL, init: RequestInit) => Response | Promise<Response>;

/**
 * Stubs global fetch with a route table keyed by "METHOD /path" (query string ignored).
 * Unknown routes answer 404 problem details so missing mocks are visible in assertions.
 * GET /api/auth/me answers a signed-in admin unless `routes` mocks it.
 */
export function mockFetch(routes: Record<string, FetchHandler | unknown>): Mock {
  const table: Record<string, FetchHandler | unknown> = { 'GET /api/auth/me': adminUser, ...routes };
  const fn = vi.fn(async (input: RequestInfo | URL, init: RequestInit = {}) => {
    const url = new URL(typeof input === 'string' ? input : input instanceof URL ? input.href : input.url, 'http://localhost');
    const method = (init.method ?? 'GET').toUpperCase();
    const key = `${method} ${url.pathname}`;
    const handler = table[key];
    if (handler === undefined) {
      return problemResponse({ title: `No mock for ${key}` }, 404);
    }
    if (typeof handler === 'function') {
      return (handler as FetchHandler)(url, init);
    }
    return jsonResponse(handler);
  });
  vi.stubGlobal('fetch', fn);
  return fn;
}

/** Calls recorded by a fetch mock as "METHOD /path". */
export function fetchCalls(fn: Mock): string[] {
  return fn.mock.calls.map(([input, init]) => {
    const url = new URL(typeof input === 'string' ? input : (input as URL).href, 'http://localhost');
    return `${((init as RequestInit | undefined)?.method ?? 'GET').toUpperCase()} ${url.pathname}`;
  });
}

/** URLs (with query strings) of the calls a fetch mock received for a "METHOD /path" route. */
export function requestUrls(fn: Mock, route: string): URL[] {
  return fn.mock.calls.flatMap(([input, init]) => {
    const url = new URL(typeof input === 'string' ? input : (input as URL).href, 'http://localhost');
    const key = `${((init as RequestInit | undefined)?.method ?? 'GET').toUpperCase()} ${url.pathname}`;
    return key === route ? [url] : [];
  });
}

/** The ApiError a promise rejects with; fails when it resolves or rejects with anything else. */
export async function rejection(promise: Promise<unknown>): Promise<ApiError> {
  try {
    await promise;
  } catch (error) {
    if (error instanceof ApiError) {
      return error;
    }
    throw error;
  }
  throw new Error('Expected the promise to reject');
}
