import { afterEach, describe, expect, it, vi } from 'vitest';
import { evalRunDto, evalRunSummary } from '../../test/evalsApi';
import { fetchCalls, jsonResponse, mockFetch, problemResponse, rejection, requestUrls } from '../../test/utils';
import { evalsApi, normalizeEvalRunListParams } from './api';

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('evalsApi', () => {
  it('lists runs with only the given parameters', async () => {
    const fetchMock = mockFetch({ 'GET /api/evals/runs': () => jsonResponse([evalRunSummary()]) });
    await expect(evalsApi.listEvalRuns({ limit: 20, dataset: ' handbook golden ' })).resolves.toHaveLength(1);
    await evalsApi.listEvalRuns();
    expect(requestUrls(fetchMock, 'GET /api/evals/runs').map((url) => url.search)).toEqual(['?limit=20&dataset=handbook+golden', '']);
    expect(normalizeEvalRunListParams({ limit: null, dataset: '  ' })).toEqual({});
  });

  it('starts, cancels, deletes and syncs with encoded names', async () => {
    const queued = evalRunSummary({ status: 'queued' });
    const fetchMock = mockFetch({
      'POST /api/evals/runs': () => jsonResponse(queued, 202),
      [`POST /api/evals/runs/${queued.id}/cancel`]: () => jsonResponse({ ...queued, status: 'cancelled' }),
      [`DELETE /api/evals/runs/${queued.id}`]: () => new Response(null, { status: 204 }),
      'POST /api/evals/datasets/my%20set/sync': { dataset: 'my set', langfuseDatasetId: 'ds', created: 1, updated: 0, unchanged: 0, archived: 0, langfuseUrl: null },
    });

    await expect(evalsApi.startEvalRun({ dataset: 'handbook-golden', judge: true, tags: ['policy'] })).resolves.toEqual(queued);
    await expect(evalsApi.cancelEvalRun(queued.id)).resolves.toMatchObject({ status: 'cancelled' });
    await expect(evalsApi.deleteEvalRun(queued.id)).resolves.toBeUndefined();
    await expect(evalsApi.syncEvalDataset('my set')).resolves.toMatchObject({ created: 1 });

    expect(fetchCalls(fetchMock)).toEqual([
      'POST /api/evals/runs',
      `POST /api/evals/runs/${queued.id}/cancel`,
      `DELETE /api/evals/runs/${queued.id}`,
      'POST /api/evals/datasets/my%20set/sync',
    ]);
    const [, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit];
    expect(JSON.parse(init.body as string)).toEqual({ dataset: 'handbook-golden', judge: true, tags: ['policy'] });
  });

  it('fills the parts of a run an older API version may not send', async () => {
    const { warnings: _warnings, ...run } = evalRunDto();
    mockFetch({ [`GET /api/evals/runs/${run.summary.id}`]: { ...run, byTag: null } });
    await expect(evalsApi.getEvalRun(run.summary.id)).resolves.toMatchObject({ warnings: [], byTag: [], items: run.items });
  });

  it('reports a sync without Langfuse as a client error', async () => {
    mockFetch({ 'POST /api/evals/datasets/handbook-golden/sync': () => problemResponse({ title: 'Bad Request', detail: 'Langfuse is not configured.' }, 400) });
    const error = await rejection(evalsApi.syncEvalDataset('handbook-golden'));
    expect(error.status).toBe(400);
    expect(error.message).toBe('Langfuse is not configured.');
  });
});
