import { act, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Route, useParams } from 'react-router';
import { afterEach, describe, expect, it, vi, type Mock } from 'vitest';
import { embeddingProfile, orchestratorProfile } from '../../test/apiFixtures';
import { evalDataset, evalDatasetList, evalRunsRoutes, evalRunSummary, LANGFUSE_DATASET_URL, runGuid, runningRunSummary } from '../../test/evalsApi';
import { fetchCalls, jsonResponse, mockFetch, problemResponse, renderWithProviders, requestUrls } from '../../test/utils';
import { EvalsPage } from './EvalsPage';
import type { EvalDatasetListDto, EvalRunSummaryDto } from './types';

afterEach(() => {
  vi.useRealTimers();
  vi.unstubAllGlobals();
});

function RunPageProbe() {
  const { id } = useParams();
  return <p>Run page {id}</p>;
}

function serve(db: { runs: EvalRunSummaryDto[] }, extra: Record<string, unknown> = {}, list: EvalDatasetListDto = evalDatasetList()) {
  return mockFetch({
    'GET /api/evals/datasets': list,
    ...evalRunsRoutes(() => db.runs),
    ...extra,
  });
}

function renderPage() {
  return renderWithProviders(<EvalsPage />, {
    route: '/evals',
    path: '/evals',
    extraRoutes: <Route path="/evals/runs/:id" element={<RunPageProbe />} />,
  });
}

async function openRunDialog(user: ReturnType<typeof userEvent.setup>, dataset = 'handbook-golden') {
  await user.click(within(await screen.findByRole('region', { name: dataset })).getByRole('button', { name: 'Run' }));
  return screen.getByRole('dialog', { name: `Run ${dataset}` });
}

function lastBody(fetchMock: Mock, route: string): unknown {
  const [method, path] = route.split(' ');
  const call = fetchMock.mock.calls
    .filter(([url, init]) => new URL(String(url), 'http://localhost').pathname === path && ((init as RequestInit | undefined)?.method ?? 'GET') === method)
    .at(-1)!;
  return JSON.parse((call[1] as RequestInit).body as string);
}

describe('EvalsPage datasets', () => {
  it('lists built-in and Langfuse datasets with their counts, tags and actions', async () => {
    serve({ runs: [] });
    renderPage();

    const builtin = await screen.findByRole('region', { name: 'handbook-golden' });
    expect(within(builtin).getByText('Built-in')).toBeInTheDocument();
    expect(within(builtin).getByText('Questions about the employee handbook.')).toBeInTheDocument();
    expect(within(builtin).getByText('12 questions · 8 documents · isolated knowledge base')).toBeInTheDocument();
    expect(within(within(builtin).getByRole('list', { name: 'Tags' })).getAllByRole('listitem').map((tag) => tag.textContent)).toEqual([
      'benefits',
      'policy',
      'unanswerable',
    ]);
    expect(within(builtin).getByRole('button', { name: 'Sync to Langfuse' })).toBeEnabled();
    expect(within(builtin).getByRole('button', { name: 'Run' })).toBeEnabled();

    const langfuse = screen.getByRole('region', { name: 'support-tickets' });
    expect(within(langfuse).getByText('Langfuse')).toBeInTheDocument();
    expect(within(langfuse).getByText('40 questions · live knowledge base')).toBeInTheDocument();
    expect(within(langfuse).queryByText(/documents/)).toBeNull();
    expect(within(langfuse).queryByRole('button', { name: 'Sync to Langfuse' })).toBeNull();
    expect(within(langfuse).getByRole('link', { name: /Open in Langfuse/ })).toHaveAttribute('href', LANGFUSE_DATASET_URL);

    expect(screen.queryByText(/stored only in the portal/)).toBeNull();
    expect(await screen.findByText('No runs yet')).toBeInTheDocument();
  });

  it('says that runs stay in the portal without Langfuse and disables syncing', async () => {
    serve({ runs: [] }, {}, evalDatasetList({ langfuseConfigured: false, datasets: [evalDataset()] }));
    renderPage();

    expect(await screen.findByText(/Langfuse is not configured, so runs are stored only in the portal/)).toBeInTheDocument();
    const sync = within(screen.getByRole('region', { name: 'handbook-golden' })).getByRole('button', { name: 'Sync to Langfuse' });
    expect(sync).toBeDisabled();
    expect(sync).toHaveAccessibleDescription('Syncing needs Langfuse.');
  });

  it('shows why Langfuse datasets could not be listed', async () => {
    serve({ runs: [] }, {}, evalDatasetList({ langfuseError: 'Langfuse returned 401 Unauthorized.', datasets: [evalDataset()] }));
    renderPage();
    expect(await screen.findByText('Langfuse datasets could not be loaded')).toBeInTheDocument();
    expect(screen.getByText('Langfuse returned 401 Unauthorized.')).toBeInTheDocument();
  });

  it('syncs a built-in dataset to Langfuse and shows what changed', async () => {
    const user = userEvent.setup();
    const fetchMock = serve({ runs: [] }, {
      'POST /api/evals/datasets/handbook-golden/sync': {
        dataset: 'handbook-golden',
        langfuseDatasetId: 'ds-9',
        created: 3,
        updated: 2,
        unchanged: 7,
        archived: 1,
        langfuseUrl: 'http://localhost:3000/project/knowledge-agent/datasets/ds-9',
      },
    });
    renderPage();

    const builtin = await screen.findByRole('region', { name: 'handbook-golden' });
    await user.click(within(builtin).getByRole('button', { name: 'Sync to Langfuse' }));

    expect(await within(builtin).findByText(/Synced to Langfuse: 3 created · 2 updated · 7 unchanged · 1 archived\./)).toBeInTheDocument();
    expect(within(builtin).getByRole('link', { name: /Open dataset in Langfuse/ })).toHaveAttribute(
      'href',
      'http://localhost:3000/project/knowledge-agent/datasets/ds-9',
    );
    expect(fetchCalls(fetchMock)).toContain('POST /api/evals/datasets/handbook-golden/sync');
    await waitFor(() => expect(requestUrls(fetchMock, 'GET /api/evals/datasets')).toHaveLength(2));
  });

  it('reports a failed sync', async () => {
    const user = userEvent.setup();
    serve({ runs: [] }, {
      'POST /api/evals/datasets/handbook-golden/sync': () => problemResponse({ title: 'Bad Request', detail: 'Langfuse rejected the credentials.' }, 400),
    });
    renderPage();
    const builtin = await screen.findByRole('region', { name: 'handbook-golden' });
    await user.click(within(builtin).getByRole('button', { name: 'Sync to Langfuse' }));
    expect(await within(builtin).findByText('Langfuse rejected the credentials.')).toBeInTheDocument();
    expect(within(builtin).getByText('Sync to Langfuse failed')).toBeInTheDocument();
  });
});

describe('EvalsPage run dialog', () => {
  it('disables the judge without an LLM and links to the providers', async () => {
    const user = userEvent.setup();
    serve({ runs: [] }, {}, evalDatasetList({ judgeAvailable: false, judgeModel: null }));
    renderPage();

    const dialog = await openRunDialog(user);
    const judge = within(dialog).getByRole('switch', { name: 'LLM judge' });
    expect(judge).toBeDisabled();
    expect(judge).not.toBeChecked();
    expect(judge).toHaveAccessibleDescription('The judge needs an LLM. Add an API key under Models → Providers.');
    expect(within(dialog).getByRole('link', { name: 'Models → Providers' })).toHaveAttribute('href', '/models#providers');
  });

  it('starts a run with the chosen options and opens it', async () => {
    const user = userEvent.setup();
    const fetchMock = serve({ runs: [] }, {
      'GET /api/models/profiles': [orchestratorProfile('openai'), orchestratorProfile('openai', { name: 'researcher' }), embeddingProfile()],
      'POST /api/evals/runs': () =>
        jsonResponse(runningRunSummary({ id: runGuid(7), status: 'queued', completedItems: 0, metrics: {}, startedAt: null }), 202),
    });
    renderPage();

    const dialog = await openRunDialog(user);
    expect(dialog).toHaveAccessibleDescription('12 questions · runs in a temporary, isolated knowledge base with the dataset’s 8 documents');
    const judge = within(dialog).getByRole('switch', { name: 'LLM judge' });
    expect(judge).toBeEnabled();
    expect(within(dialog).getByText('anthropic/claude-opus-5-5')).toBeInTheDocument();
    await user.click(judge);
    expect(judge).toBeChecked();

    await user.type(within(dialog).getByRole('textbox', { name: /^Max questions/ }), '5');
    await user.click(within(dialog).getByRole('checkbox', { name: 'unanswerable' }));
    await user.click(within(dialog).getByRole('checkbox', { name: 'policy' }));
    expect(within(dialog).getByText('Only questions with at least one of the selected tags are asked.')).toBeInTheDocument();

    await user.click(within(dialog).getByText('Advanced'));
    const profile = within(dialog).getByLabelText(/^Profile/);
    expect(profile).toBeVisible();
    expect([...dialog.querySelectorAll('datalist option')].map((option) => option.getAttribute('value'))).toEqual(['orchestrator', 'researcher']);
    await user.type(profile, 'researcher');
    await user.type(within(dialog).getByLabelText(/^Run name/), '  Baseline  ');

    await user.click(within(dialog).getByRole('button', { name: 'Start run' }));
    expect(await screen.findByText(`Run page ${runGuid(7)}`)).toBeInTheDocument();
    expect(screen.queryByRole('dialog')).toBeNull();
    expect(lastBody(fetchMock, 'POST /api/evals/runs')).toEqual({
      dataset: 'handbook-golden',
      source: 'builtin',
      judge: true,
      profile: 'researcher',
      maxItems: 5,
      tags: ['policy', 'unanswerable'],
      name: 'Baseline',
    });
  });

  it('sends only the dataset and defaults for a plain run of a Langfuse dataset', async () => {
    const user = userEvent.setup();
    const fetchMock = serve({ runs: [] }, { 'POST /api/evals/runs': () => jsonResponse(runningRunSummary({ id: runGuid(8) }), 202) });
    renderPage();

    const dialog = await openRunDialog(user, 'support-tickets');
    expect(dialog).toHaveAccessibleDescription('40 questions · runs against your live knowledge base');
    await user.click(within(dialog).getByRole('button', { name: 'Start run' }));
    expect(await screen.findByText(`Run page ${runGuid(8)}`)).toBeInTheDocument();
    expect(lastBody(fetchMock, 'POST /api/evals/runs')).toEqual({
      dataset: 'support-tickets',
      source: 'langfuse',
      judge: false,
      profile: null,
      maxItems: null,
      tags: null,
      name: null,
    });
  });

  it('validates the number of questions and shows API validation errors next to the fields', async () => {
    const user = userEvent.setup();
    const fetchMock = serve({ runs: [] }, {
      'POST /api/evals/runs': () =>
        problemResponse(
          {
            title: 'One or more validation errors occurred.',
            errors: { MaxItems: ['Must be between 1 and 12.'], Profile: ['Unknown profile "nope".'], Dataset: ['The dataset is being synced.'] },
          },
          400,
        ),
    });
    renderPage();

    const dialog = await openRunDialog(user);
    const maxItems = within(dialog).getByRole('textbox', { name: /^Max questions/ });
    await user.type(maxItems, '0');
    await user.click(within(dialog).getByRole('button', { name: 'Start run' }));
    expect(within(dialog).getByText('Enter a whole number greater than 0.')).toBeInTheDocument();
    expect(maxItems).toHaveAttribute('aria-invalid', 'true');
    expect(fetchCalls(fetchMock)).not.toContain('POST /api/evals/runs');

    await user.clear(maxItems);
    await user.type(maxItems, '50');
    await user.click(within(dialog).getByRole('button', { name: 'Start run' }));

    expect(await within(dialog).findByText('Must be between 1 and 12.')).toBeInTheDocument();
    expect(maxItems).toHaveAttribute('aria-invalid', 'true');
    expect(within(dialog).getByText('Unknown profile "nope".')).toBeVisible();
    expect(within(dialog).getByLabelText(/^Profile/)).toHaveAttribute('aria-invalid', 'true');
    expect(within(dialog).getByText('The dataset is being synced.')).toBeInTheDocument();
    expect(screen.queryByText(/Run page/)).toBeNull();
  });
});

describe('EvalsPage runs', () => {
  it('lists runs newest first with status, progress, key metrics, gate, start and duration', async () => {
    const user = userEvent.setup();
    const fetchMock = serve({
      runs: [
        evalRunSummary({ id: runGuid(1), name: 'Nightly 1', status: 'queued', completedItems: 0, metrics: {}, gate: null, startedAt: null, completedAt: null, createdAt: '2026-10-24T10:00:00Z' }),
        evalRunSummary({
          id: runGuid(2),
          name: 'Nightly 2',
          status: 'failed',
          judge: false,
          failedItems: 2,
          error: 'Embedding provider returned 429.',
          metrics: { retrieval_recall_at_5: { mean: 0.5, count: 4 } },
          gate: { passed: false, baselineSignature: null, checks: [{ metric: 'retrieval_recall_at_5', kind: 'minimum', actual: 0.5, expected: 0.8, passed: false, message: 'Too low.' }] },
          createdAt: '2026-10-25T10:00:00Z',
        }),
        evalRunSummary({ id: runGuid(3), name: 'Nightly 3', createdAt: '2026-10-26T10:00:00Z' }),
        runningRunSummary({ id: runGuid(4), name: 'Nightly 4', datasetSource: 'langfuse', dataset: 'support-tickets', createdAt: '2026-10-27T10:00:00Z' }),
      ],
    });
    renderPage();

    const table = await screen.findByRole('table');
    expect(requestUrls(fetchMock, 'GET /api/evals/runs')[0]!.search).toBe('?limit=20');
    expect(within(table).getAllByRole('columnheader').map((header) => header.textContent)).toEqual([
      'Run',
      'Status',
      'Finds information',
      'Answers correctly',
      'Backs with sources',
      'Gate',
      'Started',
      'Actions',
    ]);
    const [, running, completed, failed, queued] = within(table).getAllByRole('row') as [HTMLElement, HTMLElement, HTMLElement, HTMLElement, HTMLElement];

    expect(running).toHaveTextContent('Nightly 4');
    expect(running).toHaveTextContent('support-tickets · anthropic/claude-opus-5-5');
    expect(within(running).getByText('Running')).toHaveAttribute('data-status', 'running');
    expect(within(running).getByRole('progressbar', { name: 'Questions answered' })).toHaveAttribute('aria-valuenow', '3');
    expect(within(running).getByText('3/12')).toBeInTheDocument();
    expect(within(running).getByRole('button', { name: 'Cancel “Nightly 4”' })).toBeInTheDocument();
    expect(within(running).queryByRole('button', { name: /Delete/ })).toBeNull();

    expect(completed).toHaveTextContent('handbook-golden · anthropic/claude-opus-5-5');
    expect(within(completed).getByText('Completed')).toBeInTheDocument();
    expect(within(completed).getByRole('rowheader')).toHaveTextContent('Nightly 3');
    expect(
      within(completed)
        .getAllByRole('cell')
        .slice(1, 4)
        .map((cell) => cell.textContent),
    ).toEqual(['90%, Good', '75%, Needs work', '95%, Good']);
    expect(within(completed).getByText('Passed')).toHaveAttribute('data-gate', 'passed');
    expect(within(completed).getByText('2 min 30 s')).toBeInTheDocument();
    expect(within(completed).getByRole('button', { name: 'Delete “Nightly 3”' })).toBeInTheDocument();

    expect(failed.querySelector('[data-status="failed"]')).toHaveAttribute('title', 'Embedding provider returned 429.');
    expect(failed.querySelector('[data-gate="failed"]')).toHaveTextContent('Failed');
    expect(within(failed).getByText('2 failed questions')).toBeInTheDocument();
    expect(within(failed).getAllByRole('cell').slice(1, 4).map((cell) => cell.textContent)).toEqual(['50%, Poor', '–', '–']);

    expect(within(queued).getByText('Queued')).toHaveAttribute('data-status', 'queued');
    expect(within(queued).getByText('No gate')).toBeInTheDocument();

    await user.click(within(completed).getByText('95%'));
    expect(await screen.findByText(`Run page ${runGuid(3)}`)).toBeInTheDocument();
  });

  it('sums up the latest completed run against the run before it', async () => {
    serve({
      runs: [
        runningRunSummary({ id: runGuid(3), name: 'Nightly 3', createdAt: '2026-10-28T10:00:00Z' }),
        evalRunSummary({
          id: runGuid(2),
          name: 'Nightly 2',
          createdAt: '2026-10-27T10:00:00Z',
          gate: {
            passed: false,
            baselineSignature: null,
            checks: [
              { metric: 'answer_fact_recall', kind: 'minimum', actual: 0.75, expected: 0.8, passed: false, message: 'Too low.' },
              { metric: 'retrieval_recall_at_5', kind: 'minimum', actual: 0.9, expected: 0.8, passed: true, message: 'Fine.' },
            ],
          },
        }),
        evalRunSummary({
          id: runGuid(1),
          name: 'Nightly 1',
          createdAt: '2026-10-26T10:00:00Z',
          metrics: { retrieval_recall_at_5: { mean: 0.8, count: 10 }, citation_validity: { mean: 0.95, count: 12 } },
        }),
      ],
    });
    renderPage();

    const latest = await screen.findByRole('region', { name: 'Latest results' });
    expect(within(latest).getByRole('link', { name: 'Nightly 2' })).toHaveAttribute('href', `/evals/runs/${runGuid(2)}`);
    expect(latest).toHaveTextContent('compared with Nightly 1');
    const [gate, finds, answers, cites] = within(within(latest).getByRole('list', { name: 'Latest results' })).getAllByRole('listitem') as [
      HTMLElement,
      HTMLElement,
      HTMLElement,
      HTMLElement,
    ];
    expect(gate).toHaveTextContent('Quality gate');
    expect(gate).toHaveTextContent('Failed');
    expect(gate).toHaveTextContent('1 of 2 checks failed');
    expect(finds).toHaveTextContent('Finds the right information');
    expect(finds).toHaveTextContent('90%');
    expect(finds).toHaveTextContent('Good');
    expect(within(finds).getByText('+10 pp').closest('[data-tone]')).toHaveAttribute('data-tone', 'success');
    expect(answers).toHaveTextContent('Answers correctly');
    expect(answers).toHaveTextContent('Needs work');
    expect(answers).not.toHaveTextContent('pp');
    expect(answers).not.toHaveTextContent('Same as last run');
    expect(cites).toHaveTextContent('Same as last run');
    expect(latest).not.toHaveTextContent('±0');
  });

  it('shows no summary before a run has completed', async () => {
    serve({ runs: [runningRunSummary()] });
    renderPage();
    await screen.findByRole('table');
    expect(screen.queryByRole('region', { name: 'Latest results' })).toBeNull();
  });

  it('shows only the quality columns some run measured', async () => {
    serve({ runs: [evalRunSummary({ judge: false, metrics: { retrieval_recall_at_5: { mean: 1, count: 3 }, answer_fact_recall: { mean: 0.5, count: 3 } } })] });
    renderPage();
    const table = await screen.findByRole('table');
    expect(within(table).getByRole('columnheader', { name: 'Finds information' })).toBeInTheDocument();
    expect(within(table).getByRole('columnheader', { name: 'Answers correctly' })).toBeInTheDocument();
    expect(within(table).queryByRole('columnheader', { name: 'Backs with sources' })).toBeNull();
    expect(within(table).queryByRole('columnheader', { name: /know/ })).toBeNull();
  });

  it('polls the runs every 2 s only while a run is queued or running', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const db = { runs: [runningRunSummary({ completedItems: 2 })] };
    const fetchMock = serve(db);
    renderPage();

    expect(await screen.findByText('2/12')).toBeInTheDocument();
    expect(screen.getByText('Refreshing every 2 s')).toBeInTheDocument();
    const listRequests = () => requestUrls(fetchMock, 'GET /api/evals/runs').length;
    const before = listRequests();
    await act(() => vi.advanceTimersByTimeAsync(2000));
    await waitFor(() => expect(listRequests()).toBe(before + 1));

    db.runs = [runningRunSummary({ completedItems: 7 })];
    await act(() => vi.advanceTimersByTimeAsync(2000));
    expect(await screen.findByText('7/12')).toBeInTheDocument();

    db.runs = [evalRunSummary()];
    await act(() => vi.advanceTimersByTimeAsync(2000));
    expect(await screen.findByText('Completed')).toBeInTheDocument();
    expect(screen.queryByText('Refreshing every 2 s')).toBeNull();

    await act(() => vi.advanceTimersByTimeAsync(100));
    const idle = listRequests();
    await act(() => vi.advanceTimersByTimeAsync(10_000));
    expect(listRequests()).toBe(idle);
    expect(requestUrls(fetchMock, 'GET /api/evals/datasets')).toHaveLength(1);
  });

  it('does not poll when no run is active', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const fetchMock = serve({ runs: [evalRunSummary(), evalRunSummary({ id: runGuid(2), status: 'cancelled', completedAt: null })] });
    renderPage();
    expect(await screen.findByText('Cancelled')).toBeInTheDocument();
    expect(screen.queryByText('Refreshing every 2 s')).toBeNull();
    await act(() => vi.advanceTimersByTimeAsync(10_000));
    expect(requestUrls(fetchMock, 'GET /api/evals/runs')).toHaveLength(1);
  });

  it('cancels a running run and deletes a finished one after confirmation', async () => {
    const user = userEvent.setup();
    const db = {
      runs: [
        runningRunSummary({ id: runGuid(2), name: 'Nightly 2', createdAt: '2026-10-27T10:00:00Z' }),
        evalRunSummary({ id: runGuid(1), name: 'Nightly 1', createdAt: '2026-10-26T10:00:00Z' }),
      ],
    };
    const fetchMock = serve(db, {
      [`POST /api/evals/runs/${runGuid(2)}/cancel`]: () => {
        db.runs = db.runs.map((run) => (run.id === runGuid(2) ? { ...run, status: 'cancelled', error: 'Cancelled by a user.' } : run));
        return jsonResponse(db.runs[0]);
      },
      [`DELETE /api/evals/runs/${runGuid(1)}`]: () => {
        db.runs = db.runs.filter((run) => run.id !== runGuid(1));
        return new Response(null, { status: 204 });
      },
    });
    renderPage();

    await user.click(await screen.findByRole('button', { name: 'Cancel “Nightly 2”' }));
    expect(await screen.findByText('Cancelled')).toBeInTheDocument();
    expect(fetchCalls(fetchMock)).toContain(`POST /api/evals/runs/${runGuid(2)}/cancel`);
    expect(screen.getByRole('button', { name: 'Delete “Nightly 2”' })).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Delete “Nightly 1”' }));
    const dialog = screen.getByRole('dialog', { name: 'Delete run?' });
    expect(within(dialog).getByText(/“Nightly 1” and its results will be removed from the portal\./)).toBeInTheDocument();
    await user.click(within(dialog).getByRole('button', { name: 'Delete run' }));

    await waitFor(() => expect(screen.queryByText('Nightly 1')).toBeNull());
    expect(screen.queryByRole('dialog')).toBeNull();
    expect(fetchCalls(fetchMock)).toContain(`DELETE /api/evals/runs/${runGuid(1)}`);
    expect(screen.getByText('Nightly 2')).toBeInTheDocument();
  });

  it('keeps the confirmation open with the API error when a run cannot be deleted', async () => {
    const user = userEvent.setup();
    serve({ runs: [evalRunSummary({ name: 'Nightly 1' })] }, {
      [`DELETE /api/evals/runs/${runGuid(1)}`]: () => problemResponse({ title: 'Conflict', detail: 'The run is still running.' }, 409),
    });
    renderPage();
    await user.click(await screen.findByRole('button', { name: 'Delete “Nightly 1”' }));
    const dialog = screen.getByRole('dialog', { name: 'Delete run?' });
    await user.click(within(dialog).getByRole('button', { name: 'Delete run' }));
    expect(await within(dialog).findByText('The run is still running.')).toBeInTheDocument();
  });
});
