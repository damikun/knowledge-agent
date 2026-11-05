import { afterEach, describe, expect, it, vi } from 'vitest';
import { fetchCalls, mockFetch, problemResponse, rejection } from '../../test/utils';
import { jobsApi } from './api';
import { inngestOverview } from '../../test/systemApi';

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('jobsApi', () => {
  it('reads the engine, its dashboard and the schedules', async () => {
    const overview = inngestOverview();
    mockFetch({ 'GET /api/jobs/overview': overview });
    await expect(jobsApi.getJobsOverview()).resolves.toEqual(overview);
  });

  it('runs a schedule by its encoded name without a body', async () => {
    const fetchMock = mockFetch({ 'POST /api/jobs/schedules/nightly%20refresh/run': () => new Response(null, { status: 202 }) });

    await expect(jobsApi.runSchedule('nightly refresh')).resolves.toBeUndefined();
    expect(fetchCalls(fetchMock)).toEqual(['POST /api/jobs/schedules/nightly%20refresh/run']);
    const [, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit];
    expect(init.body).toBeNull();
  });

  it('reports an unknown schedule as not found', async () => {
    mockFetch({ 'POST /api/jobs/schedules/missing/run': () => problemResponse({ title: 'Not Found', detail: "Schedule 'missing' was not found." }, 404) });

    const error = await rejection(jobsApi.runSchedule('missing'));
    expect(error.status).toBe(404);
    expect(error.message).toBe("Schedule 'missing' was not found.");
  });
});
