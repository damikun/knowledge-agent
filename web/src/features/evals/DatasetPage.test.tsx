import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { evalDataset, evalDatasetList, evalRunsRoutes, evalRunSummary } from '../../test/evalsApi';
import { searchItem, WIDGET_CONVERSATION_ID } from '../../test/inboxApi';
import { jsonResponse, mockFetch, problemResponse, renderWithProviders, requestUrls } from '../../test/utils';
import { DatasetPage } from './DatasetPage';
import type { EvalDatasetDto, EvalDatasetItemDto } from './types';

afterEach(() => {
  vi.unstubAllGlobals();
});

const DATASET_ID = '0199c1e2-0000-7000-8000-0000000b0001';
const ITEMS = `/api/evals/datasets/${DATASET_ID}/items`;

function custom(overrides: Partial<EvalDatasetDto> = {}): EvalDatasetDto {
  return evalDataset({
    id: DATASET_ID,
    name: 'Shipping questions',
    source: 'custom',
    description: null,
    itemCount: 2,
    corpusDocuments: 0,
    tags: ['shipping'],
    updatedAt: '2026-11-04T10:00:00Z',
    ...overrides,
  });
}

function item(overrides: Partial<EvalDatasetItemDto> = {}): EvalDatasetItemDto {
  return {
    id: 'i1',
    question: 'Do you ship to Norway?',
    referenceAnswer: 'Yes, in 5 days.',
    expectedFacts: ['5 days'],
    expectedSources: [],
    answerable: true,
    tags: ['shipping'],
    conversationId: null,
    messageId: null,
    createdAt: '2026-11-04T10:00:00Z',
    updatedAt: '2026-11-04T10:00:00Z',
    source: 'manual',
    history: [],
    ...overrides,
  };
}

type Fetch = ReturnType<typeof mockFetch>;

function bodyOf(fetchMock: Fetch, method: string, path: string): unknown {
  const call = fetchMock.mock.calls.filter(([url, init]) => new URL(String(url), 'http://localhost').pathname === path && init?.method === method).at(-1)!;
  return JSON.parse(String(call[1]!.body));
}

function renderPage(routes: Record<string, unknown>, id = DATASET_ID) {
  const fetchMock = mockFetch({
    'GET /api/evals/datasets': evalDatasetList({ datasets: [custom(), evalDataset()] }),
    ...evalRunsRoutes(() => []),
    ...routes,
  });
  renderWithProviders(<DatasetPage />, { route: `/evals/datasets/${id}`, path: '/evals/datasets/:id' });
  return fetchMock;
}

describe('DatasetPage', () => {
  it('shows the questions with their expected answer, origin and tags, and the last run', async () => {
    renderPage({
      [`GET ${ITEMS}`]: [
        item(),
        item({ id: 'i2', question: 'Is the CEO married?', answerable: false, referenceAnswer: null, expectedFacts: [], tags: [], source: 'upload' }),
        item({ id: 'i3', question: 'And to Sweden?', conversationId: WIDGET_CONVERSATION_ID, source: 'conversation', history: [{ role: 'user', content: 'Norway?' }] }),
      ],
      ...evalRunsRoutes(() => [evalRunSummary({ id: 'r1', dataset: 'Shipping questions', datasetSource: 'custom', metrics: { answer_fact_recall: { mean: 0.82, count: 3 } } })]),
    });

    expect(await screen.findByRole('heading', { level: 1, name: 'Shipping questions' })).toBeInTheDocument();
    const table = await screen.findByRole('table', { name: 'Questions' });
    const rows = within(table).getAllByRole('row').slice(1);
    expect(rows).toHaveLength(3);
    expect(within(rows[0]!).getByText('Yes, in 5 days.')).toBeInTheDocument();
    expect(within(rows[0]!).getByText('Typed in')).toBeInTheDocument();
    expect(within(rows[1]!).getByText('Should say it does not know')).toBeInTheDocument();
    expect(within(rows[1]!).getByText('Upload')).toBeInTheDocument();
    expect(within(rows[2]!).getByRole('link', { name: 'Conversation' })).toHaveAttribute('href', `/inbox?only=${WIDGET_CONVERSATION_ID}`);
    expect(within(rows[2]!).getByText('Follow-up')).toBeInTheDocument();

    const summary = screen.getByRole('list', { name: 'Summary' });
    expect(within(summary).getByText('82%')).toBeInTheDocument();
    expect(within(summary).getByText('Good')).toBeInTheDocument();
    expect(within(summary).getByText('1 conversation · 1 typed in · 1 upload')).toBeInTheDocument();

    const user = userEvent.setup();
    await user.type(screen.getByRole('searchbox', { name: 'Search the questions' }), 'sweden');
    expect(within(table).getAllByRole('row')).toHaveLength(2);
    await user.clear(screen.getByRole('searchbox', { name: 'Search the questions' }));
    await user.click(within(screen.getByRole('radiogroup', { name: 'Added from' })).getByRole('radio', { name: /Upload/ }));
    expect(within(screen.getByRole('table', { name: 'Questions' })).getAllByRole('row')).toHaveLength(2);
  });

  it('pages through long datasets', async () => {
    const many = Array.from({ length: 30 }, (_, index) => item({ id: `i${index}`, question: `Question ${index + 1}?` }));
    renderPage({ [`GET ${ITEMS}`]: many });
    const user = userEvent.setup();
    expect(await screen.findByText('1 to 25 of 30')).toBeInTheDocument();
    expect(screen.queryByText('Question 26?')).toBeNull();
    await user.click(screen.getByRole('button', { name: 'Next' }));
    expect(screen.getByText('Question 26?')).toBeInTheDocument();
    expect(screen.getByText('26 to 30 of 30')).toBeInTheDocument();
  });

  it('offers the three ways to add questions when empty and adds a follow-up question typed in', async () => {
    let items: EvalDatasetItemDto[] = [];
    const fetchMock = renderPage({
      [`GET ${ITEMS}`]: () => jsonResponse(items),
      [`POST ${ITEMS}`]: (_url: URL, init: RequestInit) => {
        const body = JSON.parse(String(init.body));
        if (!body.question) {
          return problemResponse({ title: 'Bad Request', errors: { question: ['A question is required.'] } }, 400);
        }
        items = [item({ id: 'n1', ...body })];
        return jsonResponse(items[0], 201);
      },
      'GET /api/sources': { items: [{ id: 's1', title: 'Shipping policy', type: 'text', status: 'ready' }], total: 1 },
    });
    const user = userEvent.setup();
    const chooser = await screen.findByRole('list', { name: 'Ways to add questions' });
    expect(within(chooser).getAllByRole('button').map((button) => button.textContent)).toEqual([
      expect.stringContaining('From conversations'),
      expect.stringContaining('Type one in'),
      expect.stringContaining('Upload a file'),
    ]);
    expect(screen.getByRole('button', { name: 'Run' })).toBeDisabled();

    await user.click(within(chooser).getByRole('button', { name: /Type one in/ }));
    const sheet = await screen.findByRole('dialog', { name: 'Add a question' });
    await user.click(within(sheet).getByRole('button', { name: 'Add question' }));
    expect(await within(sheet).findByText('A question is required.')).toBeInTheDocument();

    await user.click(within(sheet).getByRole('button', { name: 'Make it a follow-up question' }));
    await user.type(within(sheet).getByRole('textbox', { name: 'Turn 1' }), 'Do you ship to Norway?');
    await user.type(within(sheet).getByRole('textbox', { name: 'Turn 2' }), 'Yes.');
    await user.type(within(sheet).getByRole('textbox', { name: 'Follow-up question' }), 'How long does it take?');
    await user.type(within(sheet).getByRole('textbox', { name: /Facts a good answer/ }), '5 days');
    await user.type(within(sheet).getByRole('searchbox', { name: /Sources it should cite/ }), 'Ship');
    await user.click(await within(sheet).findByRole('button', { name: 'Shipping policy' }));
    expect(within(within(sheet).getByRole('list', { name: 'Sources it should cite' })).getByText('Shipping policy')).toBeInTheDocument();
    await user.click(within(sheet).getByRole('button', { name: 'Add question' }));

    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Add a question' })).toBeNull());
    expect(bodyOf(fetchMock, 'POST', ITEMS)).toMatchObject({
      question: 'How long does it take?',
      expectedFacts: ['5 days'],
      expectedSources: ['Shipping policy'],
      source: 'manual',
      history: [
        { role: 'user', content: 'Do you ship to Norway?' },
        { role: 'assistant', content: 'Yes.' },
      ],
    });
    expect(requestUrls(fetchMock, 'GET /api/sources')[0]!.searchParams.get('status')).toBe('ready');
  });

  it('edits a question in a side sheet, duplicates one and deletes the selected ones', async () => {
    let items = [item(), item({ id: 'i2', question: 'Free shipping?' }), item({ id: 'i3', question: 'Express?' })];
    const fetchMock = renderPage({
      [`GET ${ITEMS}`]: () => jsonResponse(items),
      [`PUT ${ITEMS}/i1`]: (_url: URL, init: RequestInit) => {
        items = items.map((entry) => (entry.id === 'i1' ? { ...entry, ...JSON.parse(String(init.body)) } : entry));
        return jsonResponse(items[0]);
      },
      [`POST ${ITEMS}`]: (_url: URL, init: RequestInit) => {
        const copy = item({ id: 'copy', ...JSON.parse(String(init.body)) });
        items = [...items, copy];
        return jsonResponse(copy, 201);
      },
      [`POST ${ITEMS}/delete`]: (_url: URL, init: RequestInit) => {
        const ids: string[] = JSON.parse(String(init.body)).ids;
        items = items.filter((entry) => !ids.includes(entry.id));
        return jsonResponse({ deleted: ids.length });
      },
    });
    const user = userEvent.setup();
    await user.click(await screen.findByRole('button', { name: 'Do you ship to Norway?' }));
    const sheet = await screen.findByRole('dialog', { name: 'Edit question' });
    const answer = within(sheet).getByRole('textbox', { name: /Expected answer/ });
    await user.clear(answer);
    await user.type(answer, 'Yes, in 3 days.');
    await user.click(within(sheet).getByRole('button', { name: 'Save' }));
    await waitFor(() => expect(bodyOf(fetchMock, 'PUT', `${ITEMS}/i1`)).toMatchObject({ referenceAnswer: 'Yes, in 3 days.' }));
    expect(await screen.findByText('Yes, in 3 days.')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Actions for “Free shipping?”' }));
    await user.click(screen.getByRole('menuitem', { name: 'Duplicate' }));
    expect(await screen.findByRole('dialog', { name: 'Edit question' })).toBeInTheDocument();
    expect(bodyOf(fetchMock, 'POST', ITEMS)).toMatchObject({ question: 'Free shipping?', source: 'manual' });
    await user.click(within(screen.getByRole('dialog', { name: 'Edit question' })).getByRole('button', { name: 'Cancel' }));

    await user.click(screen.getAllByRole('checkbox', { name: 'Select “Free shipping?”' })[0]!);
    await user.click(screen.getByRole('checkbox', { name: 'Select “Express?”' }));
    const toolbar = screen.getByRole('toolbar', { name: 'Selected questions' });
    expect(within(toolbar).getByText('2 questions selected')).toBeInTheDocument();
    await user.click(within(toolbar).getByRole('button', { name: 'Delete selected' }));
    await user.click(within(await screen.findByRole('dialog', { name: 'Delete 2 questions?' })).getByRole('button', { name: 'Delete 2 questions' }));
    await waitFor(() => expect(bodyOf(fetchMock, 'POST', `${ITEMS}/delete`)).toEqual({ ids: ['i2', 'i3'] }));
    await waitFor(() => expect(screen.queryByRole('toolbar', { name: 'Selected questions' })).toBeNull());
  });

  it('uploads a CSV after mapping its columns, leaving out duplicates and rows with problems', async () => {
    const fetchMock = renderPage({
      [`GET ${ITEMS}`]: [item()],
      [`POST ${ITEMS}/batch`]: (_url: URL, init: RequestInit) => {
        const rows = JSON.parse(String(init.body)).items;
        return jsonResponse({ created: rows.length, skipped: 0, skippedRows: [], items: [] });
      },
    });
    const user = userEvent.setup();
    await user.click(await screen.findByRole('button', { name: 'Add questions' }));
    await user.click(screen.getByRole('menuitem', { name: 'Upload a file' }));
    const sheet = await screen.findByRole('dialog', { name: 'Upload questions' });
    expect(within(sheet).getByRole('link', { name: 'CSV template' })).toHaveAttribute('download', 'questions-template.csv');

    const csv = 'Text,Reply,Notes\n"Do you ship to Norway?",Yes.,x\nHow much is express?,"10 EUR, in a day",y\n,,z\nCan I pay by invoice?,No.,w\n';
    await user.upload(within(sheet).getByTestId('questions-file'), new File([csv], 'questions.csv', { type: 'text/csv' }));

    expect(await within(sheet).findByText('Choose the column that holds the question.')).toBeInTheDocument();
    expect(within(sheet).getByRole('button', { name: 'Add question' })).toBeDisabled();
    await user.click(within(sheet).getByRole('combobox', { name: 'What the column “Text” holds' }));
    await user.click(screen.getByRole('option', { name: 'Question' }));
    await user.click(within(sheet).getByRole('combobox', { name: 'What the column “Reply” holds' }));
    await user.click(screen.getByRole('option', { name: 'Expected answer' }));

    expect(within(sheet).getByText('2 ready · 1 already in the dataset · 1 row with problems')).toBeInTheDocument();
    const preview = within(sheet).getByRole('table', { name: 'Questions in the file' });
    expect(within(preview).getByText('The question is empty.')).toBeInTheDocument();
    expect(within(preview).getByText('Already in the dataset')).toBeInTheDocument();

    await user.click(within(sheet).getByRole('button', { name: 'Add 2 questions' }));
    await waitFor(() =>
      expect(bodyOf(fetchMock, 'POST', `${ITEMS}/batch`)).toEqual({
        items: [
          expect.objectContaining({ question: 'How much is express?', referenceAnswer: '10 EUR, in a day', source: 'upload' }),
          expect.objectContaining({ question: 'Can I pay by invoice?', referenceAnswer: 'No.', source: 'upload' }),
        ],
      }),
    );
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Upload questions' })).toBeNull());
  });

  it('marks the rows the API refused and keeps the sheet open', async () => {
    renderPage({
      [`GET ${ITEMS}`]: [],
      [`POST ${ITEMS}/batch`]: () => problemResponse({ title: 'Bad Request', errors: { 'items[1].tags': ['At most 20 tags.'] } }, 400),
    });
    const user = userEvent.setup();
    await user.click(await screen.findByRole('button', { name: /Upload a file/ }));
    const sheet = await screen.findByRole('dialog', { name: 'Upload questions' });
    const jsonl = [JSON.stringify({ question: 'One?' }), JSON.stringify({ question: 'Two?' })].join('\n');
    await user.upload(within(sheet).getByTestId('questions-file'), new File([jsonl], 'questions.jsonl'));
    await user.click(await within(sheet).findByRole('button', { name: 'Add 2 questions' }));
    expect(await within(sheet).findByText('At most 20 tags.')).toBeInTheDocument();
    expect(within(sheet).getByText('Nothing was added')).toBeInTheDocument();
  });

  it('picks questions from poorly rated conversations, with the answer they got as the expected one', async () => {
    const fetchMock = renderPage({
      [`GET ${ITEMS}`]: [item({ question: 'Hello?' })],
      'GET /api/conversations/search': { items: [searchItem({ rating: 2, thumbsDown: 1 })], nextCursor: null },
      [`GET /api/conversations/${WIDGET_CONVERSATION_ID}`]: {
        id: WIDGET_CONVERSATION_ID,
        title: 'Return an order',
        messages: [
          { id: 'm1', sequence: 1, author: 'visitor', authorName: null, text: 'Hello?', citations: [], turnId: null, createdAt: '2026-10-31T09:00:00Z' },
          { id: 'm2', sequence: 2, author: 'assistant', authorName: null, text: 'Hi!', citations: [], turnId: 't1', createdAt: '2026-10-31T09:00:05Z' },
          { id: 'm3', sequence: 3, author: 'visitor', authorName: null, text: 'Can I return shoes?', citations: [], turnId: null, createdAt: '2026-10-31T09:01:00Z' },
          { id: 'm4', sequence: 4, author: 'assistant', authorName: null, text: 'I am not sure.', citations: [], turnId: 't2', createdAt: '2026-10-31T09:01:05Z' },
          { id: 'm5', sequence: 5, author: 'staff', authorName: 'Ana', text: 'Yes, within 30 days.', citations: [], turnId: null, createdAt: '2026-10-31T09:03:00Z' },
        ],
      },
      [`POST ${ITEMS}/batch`]: { created: 1, skipped: 0, skippedRows: [], items: [] },
    });
    const user = userEvent.setup();
    await user.click(await screen.findByRole('button', { name: 'Add questions' }));
    await user.click(screen.getByRole('menuitem', { name: 'From conversations' }));
    const sheet = await screen.findByRole('dialog', { name: 'Add questions from conversations' });
    await waitFor(() => expect(requestUrls(fetchMock, 'GET /api/conversations/search').at(-1)!.searchParams.get('maxRating')).toBe('2'));
    expect(within(sheet).getByText('Rated 2 of 5')).toBeInTheDocument();

    await user.click(within(sheet).getByRole('button', { name: /Return an order/ }));
    const messages = await within(sheet).findByRole('list', { name: 'Messages' });
    expect(within(messages).getByText('Already in the dataset')).toBeInTheDocument();
    await user.click(within(messages).getByRole('button', { name: 'Use this question' }));
    const answer = within(messages).getByRole('textbox', { name: /Expected answer/ });
    expect(answer).toHaveValue('Yes, within 30 days.');
    await user.type(answer, ' Unworn only.');
    await user.click(within(messages).getByRole('switch', { name: /Ask it as a follow-up/ }));
    await user.click(within(sheet).getByRole('button', { name: 'Add question' }));

    await waitFor(() =>
      expect(bodyOf(fetchMock, 'POST', `${ITEMS}/batch`)).toEqual({
        items: [
          {
            question: 'Can I return shoes?',
            referenceAnswer: 'Yes, within 30 days. Unworn only.',
            conversationId: WIDGET_CONVERSATION_ID,
            messageId: 'm5',
            source: 'conversation',
            history: [
              { role: 'user', content: 'Hello?' },
              { role: 'assistant', content: 'Hi!' },
            ],
          },
        ],
      }),
    );
  });

  it('shows a built-in dataset read-only with Make a copy', async () => {
    renderPage({}, 'handbook-golden');
    expect(await screen.findByRole('heading', { level: 1, name: 'handbook-golden' })).toBeInTheDocument();
    expect(screen.getByText(/Make a copy to edit them/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Make a copy' })).toHaveClass('action-primary');
    expect(screen.queryByRole('button', { name: 'Add questions' })).toBeNull();
  });

  it('says when the dataset does not exist', async () => {
    renderPage({}, 'missing');
    expect(await screen.findByText('Dataset not found')).toBeInTheDocument();
  });
});
