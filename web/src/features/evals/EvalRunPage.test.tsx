import { act, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Route } from 'react-router';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { evalItem, evalRunConfig, evalRunDto, evalRunsRoutes, evalRunSummary, runGuid, runningRunSummary } from '../../test/evalsApi';
import { fetchCalls, jsonResponse, mockFetch, problemResponse, renderWithProviders, requestUrls } from '../../test/utils';
import { EvalRunPage } from './EvalRunPage';
import type { EvalRunDto, EvalRunSummaryDto } from './types';

afterEach(() => {
  vi.useRealTimers();
  vi.unstubAllGlobals();
});

const RUN_ID = runGuid(5);

function runFixture(): EvalRunDto {
  return evalRunDto(
    {
      id: RUN_ID,
      name: 'Prompt v4',
      createdAt: '2026-10-27T10:00:00Z',
      failedItems: 1,
      langfuseUrl: 'http://localhost:3000/project/knowledge-agent/datasets/ds-1/runs/r5',
      metrics: {
        retrieval_recall_at_5: { mean: 0.9, count: 10 },
        citation_validity: { mean: 0.95, count: 12 },
        answer_fact_recall: { mean: 0.75, count: 10 },
        answer_abstained: { mean: 1, count: 2 },
        judge_faithfulness: { mean: 0.875, count: 11 },
      },
      gate: {
        passed: false,
        baselineSignature: 'openai:text-embedding-3-small:1536|anthropic/claude-opus-5-5',
        checks: [
          { metric: 'answer_fact_recall', kind: 'minimum', actual: 0.75, expected: 0.8, passed: false, message: 'Expected facts 75% is below the minimum of 80%.' },
          { metric: 'retrieval_recall_at_5', kind: 'regression', actual: 0.9, expected: 0.78, passed: true, message: 'No regression against the baseline (80%).' },
          { metric: 'failed_items', kind: 'maximum', actual: 1, expected: 0, passed: false, message: '1 question failed.' },
          { metric: 'context_precision', kind: 'experimental' as never, actual: 0.5, expected: 0.6, passed: true, message: 'Custom check passed.' },
        ],
      },
    },
    {
      config: evalRunConfig({ maxItems: 12, tags: ['policy', 'unanswerable'] }),
      byTag: [
        { tag: 'policy', items: 5, metrics: { retrieval_recall_at_5: { mean: 1, count: 5 }, answer_fact_recall: { mean: 0.6, count: 5 } } },
        { tag: 'unanswerable', items: 2, metrics: { answer_abstained: { mean: 1, count: 2 } } },
      ],
      warnings: ["Expected source 'benefits-2025.pdf' is not in the knowledge base (items q3, q7)"],
      items: [
        evalItem(),
        evalItem({
          itemId: 'q2',
          question: 'What is the parental leave policy?',
          tags: [],
          status: 'failed',
          answer: null,
          error: 'Provider returned 500.',
          metrics: {},
          retrieved: [],
          citations: [],
          missingFacts: [],
          expectedFacts: [],
          referenceAnswer: null,
          judgement: null,
          langfuseTraceUrl: null,
          traceId: null,
        }),
      ],
    },
  );
}

function historyFixture(): EvalRunSummaryDto[] {
  return [
    evalRunSummary({ id: runGuid(6), name: 'Later run', createdAt: '2026-10-28T10:00:00Z', metrics: { retrieval_recall_at_5: { mean: 0.1, count: 10 } } }),
    runFixture().summary,
    evalRunSummary({ id: runGuid(4), name: 'Broken run', status: 'failed', createdAt: '2026-10-26T18:00:00Z', metrics: { retrieval_recall_at_5: { mean: 0.2, count: 3 } } }),
    evalRunSummary({
      id: runGuid(3),
      name: 'Prompt v3',
      createdAt: '2026-10-26T10:00:00Z',
      metrics: {
        retrieval_recall_at_5: { mean: 0.8, count: 10 },
        citation_validity: { mean: 0.95, count: 12 },
        answer_fact_recall: { mean: 0.8, count: 10 },
      },
    }),
    evalRunSummary({ id: runGuid(2), name: 'Prompt v2', createdAt: '2026-10-25T10:00:00Z', metrics: { retrieval_recall_at_5: { mean: 0.3, count: 10 } } }),
  ];
}

function renderRun() {
  return renderWithProviders(<EvalRunPage />, {
    route: `/evals/runs/${RUN_ID}`,
    path: '/evals/runs/:id',
    extraRoutes: <Route path="/evals" element={<p>Evaluations page</p>} />,
  });
}

const areaCard = (area: string) => document.querySelector<HTMLElement>(`[data-area="${area}"]`)!;
const areaCardAsync = async (area: string) => {
  await screen.findByRole('list', { name: 'Quality' });
  return areaCard(area);
};

describe('EvalRunPage', () => {
  it('shows status, configuration, warnings and the Langfuse link', async () => {
    mockFetch({ [`GET /api/evals/runs/${RUN_ID}`]: runFixture(), ...evalRunsRoutes(historyFixture) });
    renderRun();

    expect(await screen.findByRole('heading', { level: 1, name: 'Prompt v4' })).toBeInTheDocument();
    expect(screen.getByText('Completed', { selector: '[data-status]' })).toHaveAttribute('data-status', 'completed');
    expect(screen.getByText('12 of 12 questions')).toBeInTheDocument();
    expect(screen.getByText('1 failed question')).toBeInTheDocument();
    expect(screen.getByText('2 min 30 s')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /Open in Langfuse/ })).toHaveAttribute('href', 'http://localhost:3000/project/knowledge-agent/datasets/ds-1/runs/r5');
    expect(screen.getByRole('button', { name: 'More actions for this run' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Cancel run' })).toBeNull();

    expect(within(screen.getByRole('list', { name: 'Run warnings' })).getByText("Expected source 'benefits-2025.pdf' is not in the knowledge base (items q3, q7)")).toBeInTheDocument();

    const config = screen.getByRole('region', { name: 'Configuration' });
    const fact = (label: string) => within(config).getByText(label, { selector: 'dt' }).nextElementSibling;
    expect(fact('Knowledge base')).toHaveTextContent('Isolated (temporary, with the dataset’s documents)');
    expect(fact('Profile')).toHaveTextContent('orchestrator');
    expect(fact('Chat model')).toHaveTextContent('anthropic/claude-opus-5-5');
    expect(fact('Embedding model')).toHaveTextContent('openai:text-embedding-3-small:1536');
    expect(fact('Judge model')).toHaveTextContent('anthropic/claude-opus-5-5');
    expect(fact('Prompt')).toHaveTextContent('orchestrator-system v4 · langfuse');
    expect(within(config).getByRole('link', { name: 'orchestrator-system' })).toHaveAttribute('href', '/prompts/orchestrator-system');
    expect(fact('Search')).toHaveTextContent('Hybrid (vector + keyword) · reranked by voyage/rerank-2.5');
    expect(fact('Retrieval')).toHaveTextContent('Top 10 · 4 in parallel');
    expect(fact('Questions asked')).toHaveTextContent('First 12 · tagged policy, unanswerable');
  });

  it('leads with plain quality questions, verdicts and only meaningful changes versus the previous run', async () => {
    const fetchMock = mockFetch({ [`GET /api/evals/runs/${RUN_ID}`]: runFixture(), ...evalRunsRoutes(historyFixture) });
    renderRun();

    const quality = await screen.findByRole('list', { name: 'Quality' });
    expect(within(quality).getAllByRole('listitem').map((card) => card.getAttribute('data-area'))).toEqual(['finds', 'answers', 'cites', 'declines']);
    expect(await screen.findByRole('link', { name: 'Prompt v3' })).toHaveAttribute('href', `/evals/runs/${runGuid(3)}`);
    expect(requestUrls(fetchMock, 'GET /api/evals/runs').at(-1)!.search).toBe('?limit=20&dataset=handbook-golden');
    expect(screen.getByRole('region', { name: 'How the agent did' })).toHaveTextContent('12 questions scored');

    const finds = areaCard('finds');
    expect(finds).toHaveTextContent('Finds the right information');
    expect(finds).toHaveTextContent('90%');
    expect(within(finds).getByText('Good')).toHaveAttribute('data-verdict', 'good');
    expect(finds.querySelector('[data-change]')).toHaveAttribute('data-change', 'better');
    expect(finds).toHaveTextContent('+10 pp');
    expect(finds).not.toHaveTextContent('questions');
    expect(finds).not.toHaveTextContent('If it is low');

    const answers = areaCard('answers');
    expect(within(answers).getByText('Needs work')).toHaveAttribute('data-verdict', 'needs-work');
    expect(answers.querySelector('[data-change]')).toHaveAttribute('data-change', 'worse');
    expect(answers).toHaveTextContent('-5 pp');
    expect(answers).toHaveTextContent('If it is low');
    expect(answers).toHaveTextContent('Sticks to its sources87.5%');

    expect(areaCard('cites').querySelector('[data-change]')).toHaveAttribute('data-change', 'same');
    expect(areaCard('cites')).toHaveTextContent('Same as last run');
    expect(screen.queryByText(/±0/)).toBeNull();
    expect(areaCard('declines').querySelector('[data-change]')).toBeNull();
    expect(areaCard('declines')).toHaveTextContent('2 questions it should decline');
  });

  it('keeps the raw measurements in collapsed technical details with their usual names', async () => {
    mockFetch({ [`GET /api/evals/runs/${RUN_ID}`]: runFixture(), ...evalRunsRoutes(historyFixture) });
    renderRun();

    const table = await screen.findByRole('table', { name: 'Technical details' });
    expect(table.closest('details')).not.toHaveAttribute('open');
    const row = table.querySelector<HTMLElement>('[data-metric="retrieval_recall_at_5"]')!;
    expect(row).toHaveTextContent('Retrieval recall@5');
    expect(row).toHaveTextContent('Search finds the right source');
    expect(within(row).getByText('Retrieval recall@5')).toHaveAttribute('title', expect.stringContaining('top 5 results'));
    await waitFor(() => expect(row).toHaveTextContent('+10 pp'));
    expect(table.querySelector('[data-metric="citation_validity"]')).toHaveTextContent('Same');
  });

  it('links a weak area to the questions that fell short', async () => {
    const user = userEvent.setup();
    mockFetch({ [`GET /api/evals/runs/${RUN_ID}`]: runFixture(), ...evalRunsRoutes(historyFixture) });
    renderRun();

    await user.click(await within(await areaCardAsync('answers')).findByRole('button', { name: 'Show the question that fell short' }));
    const items = screen.getByRole('region', { name: 'Questions' });
    expect(within(items).getByRole('radio', { name: /Incomplete answer/ })).toBeChecked();
    expect(within(items).getByRole('row', { name: /How many vacation days/ })).toBeInTheDocument();
    expect(within(items).queryByRole('row', { name: /parental leave/ })).toBeNull();

    await user.click(within(items).getByRole('radio', { name: /Errors/ }));
    expect(within(items).getByRole('row', { name: /parental leave/ })).toBeInTheDocument();
    expect(within(items).queryByRole('row', { name: /How many vacation days/ })).toBeNull();

    await user.click(within(items).getByRole('radio', { name: /All/ }));
    expect(within(items).getAllByRole('row')).toHaveLength(3);
  });

  it('says when there is no earlier completed run to compare with', async () => {
    mockFetch({ [`GET /api/evals/runs/${RUN_ID}`]: runFixture(), ...evalRunsRoutes(() => [runFixture().summary]) });
    renderRun();
    expect(await screen.findByText(/no earlier run of this dataset to compare with/)).toBeInTheDocument();
    expect(areaCard('finds').querySelector('[data-change]')).toBeNull();
  });

  it('lists the gate checks with their values in the unit of each metric', async () => {
    mockFetch({ [`GET /api/evals/runs/${RUN_ID}`]: runFixture(), ...evalRunsRoutes(historyFixture) });
    renderRun();

    const gate = await screen.findByRole('region', { name: 'Quality gate' });
    expect(within(gate).getByText('Failed')).toHaveAttribute('data-gate', 'failed');
    expect(within(gate).getByText('openai:text-embedding-3-small:1536|anthropic/claude-opus-5-5')).toBeInTheDocument();
    const [facts, recall, failedItems, custom] = within(within(gate).getByRole('list', { name: 'Gate checks' })).getAllByRole('listitem') as [
      HTMLElement,
      HTMLElement,
      HTMLElement,
      HTMLElement,
    ];

    expect(facts).toHaveAttribute('data-passed', 'false');
    expect(facts).toHaveTextContent('Failed:');
    expect(facts).toHaveTextContent('Expected facts in the answer');
    expect(facts).toHaveTextContent('Lowest allowed');
    expect(facts).toHaveTextContent('75% · needs at least 80%');
    expect(facts).not.toHaveTextContent('is below the minimum');

    expect(recall).toHaveAttribute('data-passed', 'true');
    expect(recall).toHaveTextContent('Search finds the right source');
    expect(recall).toHaveTextContent('No drop from baseline');
    expect(recall).toHaveTextContent('90% · needs at least 78%');

    expect(failedItems).toHaveTextContent('Questions that hit an error');
    expect(failedItems).toHaveTextContent('Highest allowed');
    expect(failedItems).toHaveTextContent('1 · allows at most 0');
    expect(failedItems).not.toHaveTextContent('%');

    expect(custom).toHaveTextContent('context_precision');
    expect(custom).toHaveTextContent('experimental');
    expect(custom).toHaveTextContent('Custom check passed.');
    expect(custom).not.toHaveTextContent('needs');
  });

  it('breaks the quality questions down by tag', async () => {
    mockFetch({ [`GET /api/evals/runs/${RUN_ID}`]: runFixture(), ...evalRunsRoutes(historyFixture) });
    renderRun();

    const table = within(await screen.findByRole('region', { name: 'By tag' })).getByRole('table');
    expect(
      within(table)
        .getAllByRole('columnheader')
        .map((header) => header.textContent),
    ).toEqual(['Question', 'All12 questions', 'policy5 questions', 'unanswerable2 questions']);
    expect(
      within(table)
        .getAllByRole('rowheader')
        .map((header) => header.textContent),
    ).toEqual(['Finds the right information', 'Answers correctly', 'Backs answers with sources', 'Says “I don’t know” when it should']);
    expect(within(table).getByRole('row', { name: /^Finds the right/ })).toHaveTextContent('90%100%–');
    expect(within(table).getByRole('row', { name: /^Answers correctly/ })).toHaveTextContent('75%60%–');
    expect(within(table).getByRole('row', { name: /^Backs answers/ })).toHaveTextContent('95%––');
    expect(within(table).getByRole('row', { name: /^Says/ })).toHaveTextContent('100%–100%');
  });

  it('expands a question to show its answer, citations, retrieval, facts, reference answer and judge', async () => {
    const user = userEvent.setup();
    mockFetch({ [`GET /api/evals/runs/${RUN_ID}`]: runFixture(), ...evalRunsRoutes(historyFixture) });
    renderRun();

    const items = await screen.findByRole('region', { name: 'Questions' });
    const table = within(items).getByRole('table');
    expect(within(table).getAllByRole('columnheader').map((header) => header.textContent)).toEqual([
      'Number',
      'Question',
      'Status',
      'Finds information',
      'Answers correctly',
      'Backs with sources',
      'Duration',
    ]);
    const row = within(table).getByRole('row', { name: /How many vacation days/ });
    expect(within(row).getAllByRole('cell').slice(3, 6).map((cell) => cell.textContent)).toEqual(['100%', '50%', '66.7%']);
    expect(within(row).getByText('4.2 s')).toBeInTheDocument();
    expect(screen.queryByTestId('eval-item-details')).toBeNull();

    const toggle = within(row).getByRole('button', { name: /How many vacation days/ });
    await user.click(toggle);
    expect(toggle).toHaveAttribute('aria-expanded', 'true');
    const details = screen.getByTestId('eval-item-details');

    expect(within(details).getByText('25 days', { selector: 'strong' })).toBeInTheDocument();
    const answerChips = [...details.querySelectorAll('.prose [data-citation-state]')];
    expect(answerChips.map((chip) => [chip.textContent, chip.getAttribute('data-citation-state')])).toEqual([
      ['S1', 'expected'],
      ['S2', 'valid'],
      ['S9', 'invalid'],
    ]);
    expect(answerChips[0]).toHaveAttribute('title', 'S1 · Employee handbook · an expected source');

    const citations = within(details).getAllByRole('listitem').filter((li) => li.closest('ul')?.getAttribute('aria-label') === 'Citations');
    expect(citations.map((li) => li.textContent)).toEqual(['S1Employee handbook· valid · expected', 'S2Leave policy· valid · not expected', 'S9· invalid']);

    const retrieved = within(within(details).getByRole('list', { name: 'Retrieved sources' })).getAllByRole('listitem');
    expect(retrieved.map((li) => [li.textContent, li.hasAttribute('data-expected')])).toEqual([
      ['1.Employee handbookExpected0.83', true],
      ['2.Leave policy0.71', false],
      ['3.Benefits 2026Expected0.65', true],
    ]);
    expect(within(details).getByText('Employee handbook, Benefits 2026')).toBeInTheDocument();

    const facts = within(within(details).getByRole('list', { name: 'Expected facts' })).getAllByRole('listitem');
    expect(facts.map((li) => [li.textContent, li.hasAttribute('data-missing')])).toEqual([
      ['25 days of vacation(found)', false],
      ['Carry-over until MarchMissing', true],
    ]);
    expect(within(details).getByText('1 of 2 found')).toBeInTheDocument();
    expect(within(details).getByText('New employees receive 25 days of vacation per year; unused days carry over until March.')).toBeInTheDocument();

    const criteria = within(within(details).getByRole('list', { name: 'Judge criteria' })).getAllByRole('listitem').filter((li) => li.parentElement?.getAttribute('aria-label') === 'Judge criteria');
    expect(criteria[0]).toHaveTextContent('FaithfulnessYes100%Every claim is supported by S1 and S2.');
    expect(criteria[1]).toHaveTextContent('CorrectnessPartially50%Misses the carry-over rule.');
    expect(within(criteria[1]!).getByRole('list', { name: 'Correctness issues' })).toHaveTextContent('Does not mention that unused days carry over until March.');
    expect(within(details).getByText('Judged by anthropic/claude-opus-5-5')).toBeInTheDocument();

    expect(within(details).getByText('Tokens 1,200 in · 80 out')).toBeInTheDocument();
    expect(within(details).getByText('2 tool calls')).toBeInTheDocument();
    expect(within(details).getByText('Duration 4.2 s')).toBeInTheDocument();
    expect(within(details).getByRole('link', { name: /Open trace in Langfuse/ })).toHaveAttribute(
      'href',
      'http://localhost:3000/project/knowledge-agent/traces/trace-1',
    );

    await user.click(toggle);
    expect(screen.queryByTestId('eval-item-details')).toBeNull();
  });

  it('shows the error of a question whose agent turn failed', async () => {
    const user = userEvent.setup();
    mockFetch({ [`GET /api/evals/runs/${RUN_ID}`]: runFixture(), ...evalRunsRoutes(historyFixture) });
    renderRun();

    const row = (await screen.findAllByRole('row')).find((r) => r.textContent?.includes('What is the parental leave policy?'))!;
    expect(within(row).getByText('Failed')).toHaveAttribute('title', 'Provider returned 500.');
    await user.click(within(row).getByRole('button', { name: /parental leave/ }));
    const details = screen.getByTestId('eval-item-details');
    expect(within(details).getByText('The agent turn failed')).toBeInTheDocument();
    expect(within(details).getByText('Provider returned 500.')).toBeInTheDocument();
    expect(within(details).getByText('No answer.')).toBeInTheDocument();
    expect(within(details).queryByRole('link', { name: /trace/ })).toBeNull();
  });

  it('polls the run every 2 s while it is running and stops when it finishes', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    let run = evalRunDto(runningRunSummary({ id: RUN_ID, completedItems: 1, metrics: {} }), { items: [] });
    const fetchMock = mockFetch({
      [`GET /api/evals/runs/${RUN_ID}`]: () => jsonResponse(run),
      'GET /api/evals/runs': [],
    });
    renderRun();

    expect(await screen.findByText('1/12')).toBeInTheDocument();
    expect(screen.getByText('Refreshing every 2 s')).toBeInTheDocument();
    expect(screen.getByText('No scores yet')).toBeInTheDocument();
    expect(screen.getByText('No questions answered yet')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Cancel run' })).toBeInTheDocument();

    const runRequests = () => requestUrls(fetchMock, `GET /api/evals/runs/${RUN_ID}`).length;
    const listRequests = () => requestUrls(fetchMock, 'GET /api/evals/runs').length;
    const before = runRequests();
    await act(() => vi.advanceTimersByTimeAsync(2000));
    await waitFor(() => expect(runRequests()).toBe(before + 1));

    run = evalRunDto(runningRunSummary({ id: RUN_ID, completedItems: 6, metrics: { citation_validity: { mean: 1, count: 6 } } }), { items: [evalItem()] });
    await act(() => vi.advanceTimersByTimeAsync(2000));
    expect(await screen.findByText('6/12')).toBeInTheDocument();
    expect(areaCard('cites')).toHaveTextContent('100%');
    const listsWhileRunning = listRequests();

    run = runFixture();
    await act(() => vi.advanceTimersByTimeAsync(2000));
    expect(await screen.findByText('Completed', { selector: '[data-status]' })).toBeInTheDocument();
    expect(screen.queryByText('Refreshing every 2 s')).toBeNull();
    await waitFor(() => expect(listRequests()).toBe(listsWhileRunning + 1));

    await act(() => vi.advanceTimersByTimeAsync(100));
    const idle = runRequests();
    await act(() => vi.advanceTimersByTimeAsync(10_000));
    expect(runRequests()).toBe(idle);
  });

  it('cancels a running run from the header', async () => {
    const user = userEvent.setup();
    let run = evalRunDto(runningRunSummary({ id: RUN_ID }), { items: [] });
    const fetchMock = mockFetch({
      [`GET /api/evals/runs/${RUN_ID}`]: () => jsonResponse(run),
      [`POST /api/evals/runs/${RUN_ID}/cancel`]: () => {
        run = { ...run, summary: { ...run.summary, status: 'cancelled', error: 'Cancelled by a user.' } };
        return jsonResponse(run.summary);
      },
      'GET /api/evals/runs': [],
    });
    renderRun();

    await user.click(await screen.findByRole('button', { name: 'Cancel run' }));
    expect(await screen.findByText('Cancelled')).toBeInTheDocument();
    expect(screen.getByText('Cancelled by a user.')).toBeInTheDocument();
    expect(fetchCalls(fetchMock)).toContain(`POST /api/evals/runs/${RUN_ID}/cancel`);
    expect(screen.getByRole('button', { name: 'More actions for this run' })).toBeInTheDocument();
  });

  it('deletes a finished run after confirmation and returns to the list', async () => {
    const user = userEvent.setup();
    const fetchMock = mockFetch({
      [`GET /api/evals/runs/${RUN_ID}`]: runFixture(),
      [`DELETE /api/evals/runs/${RUN_ID}`]: () => new Response(null, { status: 204 }),
      ...evalRunsRoutes(historyFixture),
    });
    renderRun();

    await user.click(await screen.findByRole('button', { name: 'More actions for this run' }));
    await user.click(screen.getByRole('menuitem', { name: 'Delete run' }));
    const dialog = screen.getByRole('dialog', { name: 'Delete run?' });
    expect(within(dialog).getByText(/The experiment recorded in Langfuse is kept\./)).toBeInTheDocument();
    await user.click(within(dialog).getByRole('button', { name: 'Delete run' }));
    expect(await screen.findByText('Evaluations page')).toBeInTheDocument();
    expect(fetchCalls(fetchMock)).toContain(`DELETE /api/evals/runs/${RUN_ID}`);
  });

  it('says when the run does not exist', async () => {
    mockFetch({ [`GET /api/evals/runs/${RUN_ID}`]: () => problemResponse({ title: 'Not Found', detail: 'Unknown eval run.' }, 404) });
    renderRun();
    expect(await screen.findByText('Run not found')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Show all runs' })).toHaveAttribute('href', '/evals');
  });

  it('tolerates a run without warnings from an older API', async () => {
    const { warnings: _warnings, ...withoutWarnings } = runFixture();
    mockFetch({ [`GET /api/evals/runs/${RUN_ID}`]: withoutWarnings, ...evalRunsRoutes(historyFixture) });
    renderRun();
    expect(await screen.findByRole('heading', { level: 1, name: 'Prompt v4' })).toBeInTheDocument();
    expect(screen.queryByRole('list', { name: 'Run warnings' })).toBeNull();
  });
});
