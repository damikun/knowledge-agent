import { afterEach, describe, expect, it, onTestFinished, vi } from 'vitest';
import { jsonResponse, mockFetch, problemResponse, rejection } from '../test/utils';
import { ApiError, asArray, request, toApiError } from './api';
import { onForbidden, onUnauthorized } from './session';

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('toApiError', () => {
  it('uses the detail of RFC 9457 problem details', async () => {
    const error = await toApiError(
      problemResponse({ type: 'https://tools.ietf.org/html/rfc9110#section-15.6.4', title: 'Service Unavailable', detail: 'No chat provider is configured.' }, 503),
    );
    expect(error).toBeInstanceOf(ApiError);
    expect(error.status).toBe(503);
    expect(error.title).toBe('Service Unavailable');
    expect(error.detail).toBe('No chat provider is configured.');
    expect(error.message).toBe('No chat provider is configured.');
    expect(error.isValidation).toBe(false);
  });

  it('falls back to the title when there is no detail', async () => {
    const error = await toApiError(problemResponse({ title: 'Prompt not found' }, 404));
    expect(error.message).toBe('Prompt not found');
  });

  it('flattens validation problem details and exposes errors per field', async () => {
    const error = await toApiError(
      problemResponse(
        {
          title: 'One or more validation errors occurred.',
          errors: {
            Url: ['Enter an absolute http(s) URL.'],
            '$.title': ['Title is too long.', 'Title has invalid characters.'],
            SourceIds: ['Must not contain duplicates.'],
          },
        },
        400,
      ),
    );
    expect(error.status).toBe(400);
    expect(error.isValidation).toBe(true);
    expect(error.message).toBe(
      'Enter an absolute http(s) URL. Title is too long. Title has invalid characters. Source ids: Must not contain duplicates.',
    );
    expect(error.errorsFor('url')).toEqual(['Enter an absolute http(s) URL.']);
    expect(error.errorsFor('Title')).toEqual(['Title is too long.', 'Title has invalid characters.']);
    expect(error.errorsFor('content')).toEqual([]);
    expect(error.errorsExcept(['url', 'title'])).toEqual(['Must not contain duplicates.']);
  });

  it('gives a helpful message for proxy errors with an HTML body', async () => {
    const error = await toApiError(new Response('<html><body>Bad Gateway</body></html>', { status: 502, statusText: 'Bad Gateway' }));
    expect(error.status).toBe(502);
    expect(error.message).toMatch(/not reachable/i);
  });

  it('uses short plain-text bodies as the message', async () => {
    const error = await toApiError(new Response('Upload too large', { status: 413 }));
    expect(error.message).toBe('Upload too large');
  });

  it('explains a 403 as the missing admin role and a 401 as an ended session', async () => {
    const forbidden = await toApiError(problemResponse({ type: 'https://tools.ietf.org/html/rfc9110#section-15.5.4', title: 'Forbidden' }, 403));
    expect(forbidden.message).toBe('You need the admin role to do this.');
    expect((await toApiError(new Response(null, { status: 403 }))).message).toBe('You need the admin role to do this.');
    expect((await toApiError(problemResponse({ title: 'Unauthorized' }, 401))).message).toBe('Your session has ended. Sign in again.');
    const csrf = await toApiError(
      problemResponse({ title: 'Missing request header', detail: 'Requests that change data must send the X-KA-Request header.' }, 403),
    );
    expect(csrf.message).toBe('Requests that change data must send the X-KA-Request header.');
  });
});

describe('request', () => {
  it('returns parsed JSON for successful requests', async () => {
    mockFetch({ 'GET /api/system/info': { version: '1.0.0', environment: 'Development' } });
    await expect(request('/api/system/info')).resolves.toMatchObject({ version: '1.0.0' });
  });

  it('throws ApiError with field errors for validation failures', async () => {
    mockFetch({
      'POST /api/sources/url': () => problemResponse({ title: 'One or more validation errors occurred.', errors: { url: ['The URL is not valid.'] } }, 400),
    });
    const error = await rejection(request('/api/sources/url', { method: 'POST', body: { url: 'nope' } }));
    expect(error.status).toBe(400);
    expect(error.errorsFor('url')).toEqual(['The URL is not valid.']);
  });

  it('reports network failures with status 0', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => Promise.reject(new TypeError('Failed to fetch'))));
    const error = await rejection(request('/api/sources'));
    expect(error.status).toBe(0);
    expect(error.isNetworkError).toBe(true);
    expect(error.message).toMatch(/cannot reach the api/i);
  });

  it('sends the X-KA-Request header with every request, uploads included', async () => {
    const fetchMock = mockFetch({
      'GET /api/sources': [],
      'POST /api/sources/url': () => jsonResponse({ id: 's1' }, 202),
      'POST /api/sources/upload': () => jsonResponse({ id: 's2' }, 202),
      'DELETE /api/sources/s1': () => new Response(null, { status: 204 }),
    });
    const form = new FormData();
    form.append('file', new File(['Vacation policy'], 'policy.txt', { type: 'text/plain' }));

    await request('/api/sources');
    await request('/api/sources/url', { method: 'POST', body: { url: 'https://example.com' } });
    await request('/api/sources/upload', { method: 'POST', formData: form });
    await request('/api/sources/s1', { method: 'DELETE' });

    const calls = fetchMock.mock.calls as unknown as Array<[string, RequestInit]>;
    expect(calls).toHaveLength(4);
    for (const [, init] of calls) {
      expect(init.headers).toMatchObject({ 'X-KA-Request': '1' });
    }
    // The browser sets the multipart boundary itself.
    expect(calls[2]![1].headers).not.toHaveProperty('Content-Type');
  });

  it('tells the session listeners about a 401, but not about other errors', async () => {
    mockFetch({
      'GET /api/conversations': () => problemResponse({ title: 'Unauthorized' }, 401),
      'GET /api/models/profiles': () => problemResponse({ title: 'Forbidden' }, 403),
      'GET /api/sources/s9': () => problemResponse({ title: 'Source not found' }, 404),
    });
    const listener = vi.fn();
    onTestFinished(onUnauthorized(listener));

    const expired = await rejection(request('/api/conversations'));
    expect(expired.status).toBe(401);
    expect(expired.message).toBe('Your session has ended. Sign in again.');
    expect(listener).toHaveBeenCalledOnce();

    expect((await rejection(request('/api/models/profiles'))).message).toBe('You need the admin role to do this.');
    expect((await rejection(request('/api/sources/s9'))).status).toBe(404);
    expect(listener).toHaveBeenCalledOnce();
  });

  it('tells the 403 listeners about a 403, and only them', async () => {
    mockFetch({
      'GET /api/conversations': () => problemResponse({ title: 'Unauthorized' }, 401),
      'GET /api/models/profiles': () => problemResponse({ title: 'Forbidden' }, 403),
    });
    const forbidden = vi.fn();
    const unauthorized = vi.fn();
    onTestFinished(onForbidden(forbidden));
    onTestFinished(onUnauthorized(unauthorized));

    expect((await rejection(request('/api/models/profiles'))).status).toBe(403);
    expect(forbidden).toHaveBeenCalledOnce();
    expect(unauthorized).not.toHaveBeenCalled();

    expect((await rejection(request('/api/conversations'))).status).toBe(401);
    expect(forbidden).toHaveBeenCalledOnce();
    expect(unauthorized).toHaveBeenCalledOnce();
  });

  it('passes aborts through untouched', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => Promise.reject(new DOMException('Aborted', 'AbortError'))));
    await expect(request('/api/sources')).rejects.toMatchObject({ name: 'AbortError' });
  });

  it('accepts empty bodies (204 / 202) and sends JSON bodies', async () => {
    const fetchMock = mockFetch({
      'DELETE /api/sources/s1': () => new Response(null, { status: 204 }),
      'POST /api/sources/reindex': () => new Response('', { status: 202 }),
      'PUT /api/models/profiles/orchestrator': () => jsonResponse({ name: 'orchestrator' }),
    });
    await expect(request('/api/sources/s1', { method: 'DELETE' })).resolves.toBeUndefined();
    await expect(request('/api/sources/reindex', { method: 'POST' })).resolves.toBeUndefined();
    await request('/api/models/profiles/orchestrator', { method: 'PUT', body: { provider: 'openai', model: 'gpt-5-mini' } });
    const [, init] = fetchMock.mock.calls[2] as unknown as [string, RequestInit];
    expect(init.method).toBe('PUT');
    expect(init.headers).toMatchObject({ 'Content-Type': 'application/json' });
    expect(JSON.parse(init.body as string)).toEqual({ provider: 'openai', model: 'gpt-5-mini' });
  });
});

describe('asArray', () => {
  it('accepts bare arrays and common wrappers', () => {
    expect(asArray([1, 2])).toEqual([1, 2]);
    expect(asArray({ items: [3] })).toEqual([3]);
    expect(asArray({ hits: [4] })).toEqual([4]);
    expect(asArray(null)).toEqual([]);
  });
});
