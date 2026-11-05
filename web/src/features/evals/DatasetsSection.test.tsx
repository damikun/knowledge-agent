import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { Route } from 'react-router';
import { evalDataset, evalDatasetList, evalRunsRoutes, evalRunSummary, runGuid } from '../../test/evalsApi';
import { mockFetch, renderWithProviders } from '../../test/utils';
import { EvalsPage } from './EvalsPage';
import type { EvalDatasetDto } from './types';

afterEach(() => {
  vi.unstubAllGlobals();
});

const DATASET_ID = '0199c1e2-0000-7000-8000-0000000b0001';

function custom(overrides: Partial<EvalDatasetDto> = {}): EvalDatasetDto {
  return evalDataset({
    id: DATASET_ID,
    name: 'Shipping questions',
    source: 'custom',
    description: null,
    itemCount: 1,
    corpusDocuments: 0,
    tags: ['shipping'],
    updatedAt: '2026-11-04T10:00:00Z',
    ...overrides,
  });
}

function bodyOf(fetchMock: ReturnType<typeof mockFetch>, method: string, path: string): unknown {
  const call = fetchMock.mock.calls.filter(([url, init]) => new URL(String(url), 'http://localhost').pathname === path && init?.method === method).at(-1)!;
  return JSON.parse(String(call[1]!.body));
}

describe('Evaluation datasets of the team', () => {
  it('lists the team’s datasets apart from the examples and copies an example into one', async () => {
    const user = userEvent.setup();
    const fetchMock = mockFetch({
      'GET /api/evals/datasets': evalDatasetList({ datasets: [custom(), evalDataset()] }),
      'POST /api/evals/datasets': custom({ id: 'copy-1', name: 'handbook-golden copy', itemCount: 12 }),
      'GET /api/evals/datasets/copy-1/items': [],
      ...evalRunsRoutes(() => []),
    });
    renderWithProviders(<EvalsPage />, { route: '/evals', path: '/evals' });

    const mine = await screen.findByRole('list', { name: 'Your datasets' });
    const shipping = within(mine).getByRole('region', { name: 'Shipping questions' });
    expect(within(shipping).getByText('Yours')).toBeInTheDocument();
    expect(within(shipping).getByText(/1 question · live knowledge base/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'New dataset' })).toBeInTheDocument();

    await user.click(within(screen.getByRole('region', { name: 'handbook-golden' })).getByRole('button', { name: 'Make a copy' }));
    const dialog = await screen.findByRole('dialog', { name: 'Make a copy' });
    expect(within(dialog).getByRole('textbox', { name: /Name/ })).toHaveValue('handbook-golden copy');
    await user.click(within(dialog).getByRole('button', { name: 'Create dataset' }));

    await waitFor(() => expect(bodyOf(fetchMock, 'POST', '/api/evals/datasets')).toEqual({
      name: 'handbook-golden copy',
      description: null,
      copyFrom: 'handbook-golden',
      copyFromSource: 'builtin',
    }));
  });

  it('links each dataset of the team to its page with the last run’s score, and keeps built-in ones read-only', async () => {
    mockFetch({
      'GET /api/evals/datasets': evalDatasetList({ datasets: [custom(), evalDataset()] }),
      ...evalRunsRoutes(() => [
        evalRunSummary({ id: runGuid(3), dataset: 'Shipping questions', datasetSource: 'custom', metrics: { answer_fact_recall: { mean: 0.5, count: 1 } } }),
      ]),
    });
    renderWithProviders(<EvalsPage />, { route: '/evals', path: '/evals' });

    const shipping = within(await screen.findByRole('list', { name: 'Your datasets' })).getByRole('region', { name: 'Shipping questions' });
    expect(within(shipping).getByRole('link', { name: 'Shipping questions' })).toHaveAttribute('href', `/evals/datasets/${DATASET_ID}`);
    expect(within(shipping).getByRole('link', { name: /Open questions/ })).toHaveAttribute('href', `/evals/datasets/${DATASET_ID}`);
    await waitFor(() => expect(within(shipping).getByText('50%')).toBeInTheDocument());
    expect(within(shipping).getByText('Poor')).toBeInTheDocument();

    const builtin = screen.getByRole('region', { name: 'handbook-golden' });
    expect(within(builtin).getByText('Read-only. Make a copy to change the questions.')).toBeInTheDocument();
    expect(within(builtin).getByText('No recent runs')).toBeInTheDocument();
    expect(within(builtin).queryByRole('button', { name: /Actions for/ })).toBeNull();
  });

  it('creates a dataset from the page header and opens it', async () => {
    const user = userEvent.setup();
    mockFetch({
      'GET /api/evals/datasets': evalDatasetList({ datasets: [] }),
      'POST /api/evals/datasets': custom({ itemCount: 0 }),
      ...evalRunsRoutes(() => []),
    });
    renderWithProviders(<EvalsPage />, {
      route: '/evals',
      path: '/evals',
      extraRoutes: <Route path="/evals/datasets/:id" element={<p>Dataset page</p>} />,
    });

    await user.click(await screen.findByRole('button', { name: 'New dataset' }));
    const dialog = await screen.findByRole('dialog', { name: 'New dataset' });
    await user.type(within(dialog).getByRole('textbox', { name: /Name/ }), 'Shipping questions');
    await user.click(within(dialog).getByRole('button', { name: 'Create dataset' }));
    expect(await screen.findByText('Dataset page')).toBeInTheDocument();
  });

  it('sends the old ?dataset= links to the dataset page', async () => {
    mockFetch({ 'GET /api/evals/datasets': evalDatasetList({ datasets: [custom()] }), ...evalRunsRoutes(() => []) });
    renderWithProviders(<EvalsPage />, {
      route: `/evals?dataset=${DATASET_ID}`,
      path: '/evals',
      extraRoutes: <Route path="/evals/datasets/:id" element={<p>Dataset page</p>} />,
    });
    expect(await screen.findByText('Dataset page')).toBeInTheDocument();
  });
});
