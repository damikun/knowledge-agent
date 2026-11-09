import { act, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { embeddingProfile, orchestratorProfile } from '../../test/apiFixtures';
import { sourceDto } from '../../test/fixtures';
import { numberedSources, sourceGuid, sourcesRoutes, sourceStats } from '../../test/sourcesApi';
import { fetchCalls, jsonResponse, mockFetch, renderWithProviders, requestUrls } from '../../test/utils';
import { SourcesPage } from './SourcesPage';
import type { SourceDto } from './types';

afterEach(() => {
  vi.useRealTimers();
  vi.unstubAllGlobals();
});

/** Serves `db.sources` (changes are visible to later requests) plus the embedding profile. */
function serve(db: { sources: SourceDto[] }, extra: Record<string, unknown> = {}) {
  return mockFetch({
    ...sourcesRoutes(() => db.sources),
    'GET /api/models/profiles': [orchestratorProfile('openai'), embeddingProfile()],
    ...extra,
  });
}

const sourceRequests = (fetchMock: ReturnType<typeof mockFetch>) => requestUrls(fetchMock, 'GET /api/sources');
const lastSourceRequest = (fetchMock: ReturnType<typeof mockFetch>) => sourceRequests(fetchMock).at(-1)!;
/** The library summary under the page title, on one line. */
const summary = () =>
  within(screen.getByRole('list', { name: 'Library' }))
    .getAllByRole('listitem')
    .map((item) => item.textContent)
    .join(' · ');
const expectSummary = (text: string) => waitFor(() => expect(summary()).toBe(text));

/** A status chip by its label; the chips also show counts. */
const chip = (label: string) => screen.getByRole('radio', { name: new RegExp(`^${label}(,|$)`) });

/** Opens a Select by its name and chooses an option. */
async function choose(user: ReturnType<typeof userEvent.setup>, select: string, option: string) {
  await user.click(screen.getByRole('combobox', { name: select }));
  await user.click(within(screen.getByRole('listbox', { name: select })).getByRole('option', { name: option }));
}

/** Opens a row's menu and chooses one of its actions. */
async function rowAction(user: ReturnType<typeof userEvent.setup>, title: string, action: string) {
  await user.click(await screen.findByRole('button', { name: `Actions for “${title}”` }));
  await user.click(within(screen.getByRole('menu', { name: `Actions for “${title}”` })).getByRole('menuitem', { name: action }));
}

describe('SourcesPage', () => {
  it('lists sources with status, counts, embedding model and failure details', async () => {
    serve({
      sources: [
        sourceDto(),
        sourceDto({ id: 'web', type: 'url', title: 'Leave policy', url: 'https://intranet.example.com/leave', fileName: null, pageCount: null }),
        sourceDto({
          id: 'bad',
          type: 'text',
          title: 'Broken notes',
          status: 'failed',
          error: 'Embedding provider returned 429.',
          chunkCount: 0,
          embeddingModel: null,
        }),
      ],
    });
    renderWithProviders(<SourcesPage />);

    const table = await screen.findByRole('table');
    const rows = within(table).getAllByRole('row');
    expect(rows).toHaveLength(4);
    expect(within(table).getByRole('link', { name: /Leave policy/ })).toHaveAttribute('href', 'https://intranet.example.com/leave');
    expect(within(table).getAllByText('Embedding provider returned 429.').length).toBeGreaterThan(0);
    // The model, pages and tokens wait in the details, so the list keeps title, status, chunks and date.
    expect(
      within(table)
        .getAllByRole('columnheader')
        .map((header) => header.textContent),
    ).toEqual(['', 'Title', 'Status', 'Chunks', 'Added', 'Actions']);
    expect(within(table).queryByText('openai:text-embedding-3-small:1536')).toBeNull();
    await expectSummary('3 sources · 2 ready · 1 needs attention');
    // The status chips carry the same counts.
    expect(chip('All')).toHaveAccessibleName(/^All, ?3$/);
    expect(chip('Ready')).toHaveAccessibleName(/^Ready, ?2$/);
    expect(chip('Needs attention')).toHaveAccessibleName(/^Needs attention, ?1$/);
    expect(screen.getByText('Showing 1–3 of 3')).toBeInTheDocument();
    // A single page needs no paging controls.
    expect(screen.queryByRole('button', { name: 'Next' })).toBeNull();
  });

  it('labels sources with passages that look written to the assistant', async () => {
    serve({ sources: [sourceDto({ metadata: { suspiciousChunks: '3' } }), sourceDto({ id: 'clean', title: 'Clean notes' })] });
    renderWithProviders(<SourcesPage />);

    const table = await screen.findByRole('table');
    const [, flagged, clean] = within(table).getAllByRole('row');
    expect(within(flagged!).getByText('Suspicious content')).toHaveAttribute(
      'title',
      '3 passages look written to the assistant. Read them before you rely on this source.',
    );
    expect(within(clean!).queryByText('Suspicious content')).not.toBeInTheDocument();
  });

  it('shows the library counts from the stats, not from the loaded page', async () => {
    serve(
      { sources: [sourceDto()] },
      {
        'GET /api/sources/stats': {
          total: 1240,
          ready: 1200,
          pending: 4,
          processing: 8,
          failed: 3,
          readyChunks: 52_000,
          readyByType: {},
          readyByEmbeddingModel: {},
        },
      },
    );
    renderWithProviders(<SourcesPage />);
    await expectSummary('1,240 sources · 1,200 ready · 12 indexing · 3 need attention');
    expect(screen.getByText('Refreshing every 2 s')).toBeInTheDocument();
  });

  it('pages through the library 25 sources at a time', async () => {
    const user = userEvent.setup();
    const fetchMock = serve({ sources: numberedSources(60) });
    renderWithProviders(<SourcesPage />);

    expect(await screen.findByText('Showing 1–25 of 60')).toBeInTheDocument();
    expect(within(screen.getByRole('table')).getAllByRole('row')).toHaveLength(26);
    // Newest first.
    expect(within(screen.getByRole('table')).getAllByRole('row')[1]).toHaveTextContent('Source 60');
    expect(screen.getByRole('button', { name: 'Previous' })).toBeDisabled();
    expect(lastSourceRequest(fetchMock).search).toBe('?limit=25');

    await user.click(screen.getByRole('button', { name: 'Next' }));
    expect(await screen.findByText('Showing 26–50 of 60')).toBeInTheDocument();
    expect(lastSourceRequest(fetchMock).search).toBe('?offset=25&limit=25');
    expect(screen.getByText('Source 35')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Next' }));
    expect(await screen.findByText('Showing 51–60 of 60')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Next' })).toBeDisabled();

    await user.click(screen.getByRole('button', { name: 'Previous' }));
    expect(await screen.findByText('Showing 26–50 of 60')).toBeInTheDocument();
  });

  it('debounces the search, sends it as query and starts again at the first page', async () => {
    const user = userEvent.setup();
    const fetchMock = serve({
      sources: [...numberedSources(40), sourceDto({ id: sourceGuid(99), title: 'Employee handbook', createdAt: '2026-09-01T00:00:00Z' })],
    });
    renderWithProviders(<SourcesPage />);

    await user.click(await screen.findByRole('button', { name: 'Next' }));
    expect(await screen.findByText('Showing 26–41 of 41')).toBeInTheDocument();

    await user.type(screen.getByRole('searchbox', { name: 'Search sources' }), 'handbook');
    // Nothing is sent while typing.
    expect(sourceRequests(fetchMock).filter((url) => url.searchParams.has('query'))).toEqual([]);

    expect(await screen.findByText('Showing 1–1 of 1')).toBeInTheDocument();
    const searches = sourceRequests(fetchMock).filter((url) => url.searchParams.has('query'));
    expect(searches.map((url) => url.searchParams.get('query'))).toEqual(['handbook']);
    expect(searches[0]!.searchParams.has('offset')).toBe(false);
    expect(within(screen.getByRole('table')).getByText('Employee handbook')).toBeInTheDocument();
  });

  it('starts at the first page again when the filters are cleared', async () => {
    const user = userEvent.setup();
    const fetchMock = serve({ sources: numberedSources(40) });
    renderWithProviders(<SourcesPage />);

    await user.click(await screen.findByRole('button', { name: 'Next' }));
    expect(await screen.findByText('Showing 26–40 of 40')).toBeInTheDocument();

    await user.type(screen.getByRole('searchbox', { name: 'Search sources' }), 'Source 1');
    expect(await screen.findByText('Showing 1–13 of 13')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Clear' }));
    expect(await screen.findByText('Showing 1–25 of 40')).toBeInTheDocument();
    expect(lastSourceRequest(fetchMock).search).toBe('?limit=25');
  });

  it('maps the type filter and the status chips to query parameters', async () => {
    const user = userEvent.setup();
    const fetchMock = serve({
      sources: [
        ...numberedSources(30),
        sourceDto({ id: 'queued', title: 'Queued page', type: 'url', status: 'pending', fileName: null }),
        sourceDto({ id: 'working', title: 'Working notes', type: 'markdown', status: 'processing' }),
        sourceDto({ id: 'broken', title: 'Broken notes', type: 'text', status: 'failed' }),
      ],
    });
    renderWithProviders(<SourcesPage />);
    await user.click(await screen.findByRole('button', { name: 'Next' }));
    await screen.findByText('Showing 26–33 of 33');

    expect(chip('All')).toBeChecked();
    await user.click(chip('Indexing'));
    expect(await screen.findByText('Showing 1–2 of 2')).toBeInTheDocument();
    expect(lastSourceRequest(fetchMock).searchParams.get('status')).toBe('pending,processing');
    expect(lastSourceRequest(fetchMock).searchParams.has('offset')).toBe(false);
    expect(screen.getByText('Queued page')).toBeInTheDocument();
    expect(screen.getByText('Working notes')).toBeInTheDocument();

    await choose(user, 'Type', 'Web page');
    expect(await screen.findByText('Showing 1–1 of 1')).toBeInTheDocument();
    expect(Object.fromEntries(lastSourceRequest(fetchMock).searchParams)).toEqual({ type: 'url', status: 'pending,processing', limit: '25' });

    await user.click(chip('Needs attention'));
    await choose(user, 'Type', 'Text');
    expect(await screen.findByText('Broken notes')).toBeInTheDocument();
    expect(Object.fromEntries(lastSourceRequest(fetchMock).searchParams)).toEqual({ type: 'text', status: 'failed,waiting', limit: '25' });

    await user.click(chip('Ready'));
    await choose(user, 'Type', 'All types');
    expect(await screen.findByText('Showing 1–25 of 30')).toBeInTheDocument();
    expect(lastSourceRequest(fetchMock).searchParams.get('status')).toBe('ready');
  });

  it('guides to the first source when nothing has been added yet', async () => {
    const user = userEvent.setup();
    serve({ sources: [] });
    renderWithProviders(<SourcesPage />);
    expect(await screen.findByText('Add your first source')).toBeInTheDocument();
    // Nothing to sum up or filter yet.
    expect(screen.queryByRole('list', { name: 'Library' })).toBeNull();
    expect(screen.queryByRole('searchbox', { name: 'Search sources' })).toBeNull();
    expect(screen.queryByRole('radiogroup', { name: 'Status' })).toBeNull();
    expect(screen.queryByRole('button', { name: 'Clear filters' })).toBeNull();

    // Each way to add knowledge opens the sheet on its form.
    await user.click(screen.getByRole('button', { name: /^Files/ }));
    const sheet = screen.getByRole('dialog', { name: 'Add a source' });
    expect(within(sheet).getByRole('radio', { name: 'Files' })).toBeChecked();
    expect(within(sheet).getByRole('radio', { name: 'Files' })).toHaveAccessibleDescription('PDF, TXT or Markdown');
    expect(within(sheet).getByRole('button', { name: /^Upload files/ })).toBeInTheDocument();
  });

  it('tells a search without matches apart from an empty library and clears the filters', async () => {
    const user = userEvent.setup();
    const fetchMock = serve({ sources: numberedSources(3) });
    renderWithProviders(<SourcesPage />);
    await screen.findByText('Showing 1–3 of 3');

    await choose(user, 'Type', 'Markdown');
    await user.type(screen.getByRole('searchbox', { name: 'Search sources' }), 'quarterly report');
    expect(await screen.findByText(/Nothing matches “quarterly report”/)).toBeInTheDocument();
    expect(screen.getByText('No sources match your filters')).toBeInTheDocument();
    expect(screen.queryByText('Add your first source')).toBeNull();

    await user.click(screen.getByRole('button', { name: 'Clear filters' }));
    expect(await screen.findByText('Showing 1–3 of 3')).toBeInTheDocument();
    expect(screen.getByRole('searchbox', { name: 'Search sources' })).toHaveValue('');
    expect(screen.getByRole('combobox', { name: 'Type' })).toHaveTextContent('All types');
    expect(lastSourceRequest(fetchMock).search).toBe('?limit=25');
  });

  it('moves back a page when the last source of a page is deleted', async () => {
    const user = userEvent.setup();
    const db = { sources: numberedSources(26) };
    const fetchMock = serve(db, {
      [`DELETE /api/sources/${sourceGuid(1)}`]: () => {
        db.sources = db.sources.filter((s) => s.id !== sourceGuid(1));
        return new Response(null, { status: 204 });
      },
    });
    renderWithProviders(<SourcesPage />);
    await user.click(await screen.findByRole('button', { name: 'Next' }));
    expect(await screen.findByText('Showing 26–26 of 26')).toBeInTheDocument();

    await rowAction(user, 'Source 1', 'Delete');
    await user.click(within(await screen.findByRole('dialog', { name: 'Delete source?' })).getByRole('button', { name: 'Delete source' }));

    expect(await screen.findByText('Showing 1–25 of 25')).toBeInTheDocument();
    expect(fetchCalls(fetchMock)).toContain(`DELETE /api/sources/${sourceGuid(1)}`);
    expect(screen.queryByText(/No sources|Add your first source/)).toBeNull();
    expect(screen.queryByRole('button', { name: 'Next' })).toBeNull();
    expect(lastSourceRequest(fetchMock).searchParams.has('offset')).toBe(false);
  });

  it('deletes a source after confirmation', async () => {
    const user = userEvent.setup();
    const db = { sources: [sourceDto()] };
    const fetchMock = serve(db, {
      'DELETE /api/sources/src-handbook': () => {
        db.sources = [];
        return new Response(null, { status: 204 });
      },
    });
    renderWithProviders(<SourcesPage />);
    await rowAction(user, 'Employee handbook', 'Delete');
    const dialog = await screen.findByRole('dialog', { name: 'Delete source?' });
    await user.click(within(dialog).getByRole('button', { name: 'Delete source' }));
    expect(await screen.findByText('Add your first source')).toBeInTheDocument();
    expect(fetchCalls(fetchMock)).toContain('DELETE /api/sources/src-handbook');
  });

  it('polls the stats and the visible page every 2 s only while sources are ingesting', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const db = { sources: [sourceDto({ status: 'processing', chunkCount: 0, embeddingModel: null })] };
    const fetchMock = serve(db);
    renderWithProviders(<SourcesPage />);
    await expectSummary('1 source · 0 ready · 1 indexing');
    expect(screen.getByText('Refreshing every 2 s')).toBeInTheDocument();
    await screen.findByRole('table');

    const counts = () => ({ pages: sourceRequests(fetchMock).length, stats: requestUrls(fetchMock, 'GET /api/sources/stats').length });
    const before = counts();
    await act(() => vi.advanceTimersByTimeAsync(2000));
    await waitFor(() => expect(counts()).toEqual({ pages: before.pages + 1, stats: before.stats + 1 }));

    // Ingestion finishes: the next poll shows it and polling stops.
    db.sources = [sourceDto()];
    await act(() => vi.advanceTimersByTimeAsync(2000));
    await expectSummary('1 source · 1 ready');
    await waitFor(() => expect(within(screen.getByRole('table')).getByText('Ready')).toBeInTheDocument());
    expect(screen.queryByText('Refreshing every 2 s')).toBeNull();

    await act(() => vi.advanceTimersByTimeAsync(100));
    const idle = counts();
    await act(() => vi.advanceTimersByTimeAsync(10_000));
    expect(counts()).toEqual(idle);
  });

  it('keeps polling the page while a visible source is ingesting, even when the stats are already idle', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    // The page was read just before ingestion finished, the stats just after.
    const db = { sources: [sourceDto({ status: 'processing', chunkCount: 0, embeddingModel: null })] };
    const fetchMock = serve(db, { 'GET /api/sources/stats': () => jsonResponse(sourceStats([sourceDto()])) });
    renderWithProviders(<SourcesPage />);
    await expectSummary('1 source · 1 ready');
    expect(await within(await screen.findByRole('table')).findByText('Processing')).toBeInTheDocument();

    db.sources = [sourceDto()];
    await act(() => vi.advanceTimersByTimeAsync(2000));
    await waitFor(() => expect(within(screen.getByRole('table')).getByText('Ready')).toBeInTheDocument());

    await act(() => vi.advanceTimersByTimeAsync(100));
    const idle = sourceRequests(fetchMock).length;
    await act(() => vi.advanceTimersByTimeAsync(10_000));
    expect(sourceRequests(fetchMock)).toHaveLength(idle);
  });

  it('shows the stage of processing sources, with a progress bar while embedding', async () => {
    const processing = { status: 'processing', chunkCount: 0, embeddedChunks: 0, embeddingModel: null } as const;
    serve({
      sources: [
        sourceDto({
          ...processing,
          id: 'page',
          type: 'url',
          title: 'Leave policy',
          url: 'https://intranet.example.com/leave',
          fileName: null,
          stage: 'fetching',
        }),
        sourceDto({ ...processing, id: 'guide', title: 'Travel guide', stage: 'extracting' }),
        sourceDto({ ...processing, id: 'rules', title: 'Expense rules', stage: 'chunking' }),
        sourceDto({ ...processing, id: 'handbook', title: 'Employee handbook', stage: 'embedding', chunkCount: 400, embeddedChunks: 120 }),
        sourceDto({ ...processing, id: 'security', title: 'Security policy', stage: 'storing', chunkCount: 400, embeddedChunks: 400 }),
        sourceDto({ ...processing, id: 'notes', title: 'Onboarding notes', stage: null }),
      ],
    });
    renderWithProviders(<SourcesPage />);

    const table = await screen.findByRole('table');
    const row = (title: string) => within(table).getByRole('row', { name: new RegExp(title) });
    expect(within(row('Leave policy')).getByText('Fetching')).toHaveAttribute('data-status', 'processing');
    expect(within(row('Travel guide')).getByText('Extracting')).toBeInTheDocument();
    expect(within(row('Expense rules')).getByText('Chunking')).toBeInTheDocument();
    expect(within(row('Employee handbook')).getByText('Embedding 120 of 400')).toBeInTheDocument();
    const progress = within(row('Employee handbook')).getByRole('progressbar', { name: 'Chunks embedded' });
    expect(progress).toHaveAttribute('aria-valuenow', '120');
    expect(progress).toHaveAttribute('aria-valuemax', '400');
    expect(within(row('Security policy')).getByText('Storing')).toBeInTheDocument();
    expect(within(row('Onboarding notes')).getByText('Processing')).toBeInTheDocument();
    expect(within(table).getAllByRole('progressbar')).toHaveLength(1);
  });

  it('follows the stage and the embedding progress while it polls', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const embedding = (embeddedChunks: number) =>
      sourceDto({ status: 'processing', stage: 'embedding', chunkCount: 400, embeddedChunks, embeddingModel: null });
    const db = { sources: [embedding(120)] };
    serve(db);
    renderWithProviders(<SourcesPage />);
    const table = await screen.findByRole('table');
    expect(await within(table).findByText('Embedding 120 of 400')).toBeInTheDocument();

    db.sources = [embedding(280)];
    await act(() => vi.advanceTimersByTimeAsync(2000));
    expect(await within(table).findByText('Embedding 280 of 400')).toBeInTheDocument();
    expect(within(table).getByRole('progressbar', { name: 'Chunks embedded' })).toHaveAttribute('aria-valuenow', '280');

    db.sources = [sourceDto({ status: 'processing', stage: 'storing', chunkCount: 400, embeddedChunks: 400, embeddingModel: null })];
    await act(() => vi.advanceTimersByTimeAsync(2000));
    expect(await within(table).findByText('Storing')).toBeInTheDocument();
    expect(within(table).queryByRole('progressbar')).toBeNull();

    db.sources = [sourceDto({ chunkCount: 400, embeddedChunks: 400 })];
    await act(() => vi.advanceTimersByTimeAsync(2000));
    expect(await within(table).findByText('Ready')).toBeInTheDocument();
  });

  it('links a source to its last job run in the Inngest dashboard from its details, not from the table', async () => {
    const user = userEvent.setup();
    const failedRun = 'http://localhost:8288/run?runID=01M66AE49V11B6EF9BKZ5D4M0S';
    serve({
      sources: [
        sourceDto({
          id: 'bad',
          title: 'Broken notes',
          status: 'failed',
          error: 'Embedding provider returned 429.',
          attempts: 3,
          jobRunId: '01M66AE49V11B6EF9BKZ5D4M0S',
          jobRunUrl: failedRun,
        }),
        sourceDto({ id: 'local', title: 'Meeting notes' }),
      ],
    });
    renderWithProviders(<SourcesPage />);

    await user.click(await screen.findByRole('button', { name: 'Details of “Broken notes”' }));
    const run = await screen.findByRole('link', { name: /Open in the Inngest dashboard/ });
    expect(run).toHaveAttribute('href', failedRun);
    expect(run).toHaveAttribute('target', '_blank');
    expect(run).toHaveAttribute('rel', 'noopener noreferrer');
    expect(within(screen.getByRole('table')).queryByRole('link', { name: /View run/ })).toBeNull();
  });

  it('shows only the source linked from a citation until "Show all sources"', async () => {
    const user = userEvent.setup();
    const fetchMock = serve({ sources: numberedSources(30) });
    renderWithProviders(<SourcesPage />, { route: `/sources?source=${sourceGuid(3)}`, path: '/sources' });

    expect(await screen.findByText('Showing the source linked from a citation.')).toBeInTheDocument();
    expect(await screen.findByText('Showing 1–1 of 1')).toBeInTheDocument();
    expect(lastSourceRequest(fetchMock).searchParams.get('ids')).toBe(sourceGuid(3));
    const [header, row] = within(screen.getByRole('table')).getAllByRole('row');
    expect(header).toBeDefined();
    expect(row).toHaveTextContent('Source 3');
    expect(row).toHaveAttribute('data-highlighted', 'true');

    await user.click(screen.getByRole('button', { name: 'Show all sources' }));
    expect(await screen.findByText('Showing 1–25 of 30')).toBeInTheDocument();
    expect(lastSourceRequest(fetchMock).searchParams.has('ids')).toBe(false);
  });

  it('offers "Re-index all" with the number of stale sources from the stats', async () => {
    const user = userEvent.setup();
    const fetchMock = serve(
      { sources: numberedSources(3) },
      {
        'GET /api/sources/stats': {
          ...sourceStats(numberedSources(3)),
          total: 1240,
          ready: 1240,
          readyByEmbeddingModel: { 'openai:text-embedding-3-small:1536': 1200, 'voyage:voyage-4:1024': 38, 'ollama:nomic-embed-text:768': 2 },
        },
        'POST /api/sources/reindex': { queued: 1240 },
      },
    );
    renderWithProviders(<SourcesPage />);

    expect(await screen.findByText('40 sources embedded with a different model')).toBeInTheDocument();
    expect(screen.getByText('voyage:voyage-4:1024, ollama:nomic-embed-text:768', { selector: 'code' })).toBeInTheDocument();
    expect(screen.getByText('openai:text-embedding-3-small:1536', { selector: 'code' })).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Re-index all' }));
    expect(await screen.findByText(/Re-indexing started for 1,240 sources/)).toBeInTheDocument();
    expect(fetchCalls(fetchMock)).toContain('POST /api/sources/reindex');
  });

  it('does not show the notice when every ready source uses the current model', async () => {
    serve({ sources: [sourceDto(), sourceDto({ id: 'queued', status: 'pending', embeddingModel: 'voyage:voyage-4:1024' })] });
    renderWithProviders(<SourcesPage />);
    await screen.findByRole('table');
    await expectSummary('2 sources · 1 ready · 1 indexing');
    expect(screen.queryByText(/embedded with a different model/)).toBeNull();
  });

  it('explains sources that wait for an embedding model and filters them with the ones that need attention', async () => {
    const user = userEvent.setup();
    const reason = "No configured embedding provider is available for the 'embedding' profile (Provider = auto).";
    const waiting = { status: 'waiting', chunkCount: 0, embeddedChunks: 0, embeddingModel: null, error: reason } as const;
    const fetchMock = serve({
      sources: [
        sourceDto(),
        sourceDto({ ...waiting, id: 'guide', title: 'Travel guide' }),
        sourceDto({ ...waiting, id: 'rules', title: 'Expense rules' }),
      ],
    });
    renderWithProviders(<SourcesPage />);

    await expectSummary('3 sources · 1 ready · 2 need attention');
    const notice = screen.getByText(/2 sources wait for an embedding model\./);
    expect(notice).toHaveTextContent('2 sources wait for an embedding model. Connect one on the AI models page and they are queued automatically.');
    expect(within(notice).getByRole('link', { name: 'AI models page' })).toHaveAttribute('href', '/models#providers');

    const table = await screen.findByRole('table');
    const badge = within(within(table).getByRole('row', { name: /Travel guide/ })).getByText('Waiting for a model');
    expect(badge).toHaveAttribute('title', reason);
    // Nothing moves until an embedding model is saved, so the page does not poll.
    expect(screen.queryByText('Refreshing every 2 s')).toBeNull();

    await user.click(chip('Needs attention'));
    expect(await screen.findByText('Showing 1–2 of 2')).toBeInTheDocument();
    expect(lastSourceRequest(fetchMock).searchParams.get('status')).toBe('failed,waiting');
  });

  it('words the notice for a single waiting source', async () => {
    serve({ sources: [sourceDto({ status: 'waiting', chunkCount: 0, embeddingModel: null, error: 'No embedding model.' })] });
    renderWithProviders(<SourcesPage />);
    const notice = await screen.findByText(/1 source waits for an embedding model\./);
    expect(notice).toHaveTextContent('1 source waits for an embedding model. Connect one on the AI models page and it is queued automatically.');
  });

  it('shows no waiting notice when no source waits', async () => {
    serve({ sources: [sourceDto(), sourceDto({ id: 'bad', title: 'Broken notes', status: 'failed', error: 'Timeout' })] });
    renderWithProviders(<SourcesPage />);
    await expectSummary('2 sources · 1 ready · 1 needs attention');
    expect(screen.queryByText(/queued automatically/)).toBeNull();
  });

  it('adds a web page and surfaces validation errors on the field', async () => {
    const user = userEvent.setup();
    let attempts = 0;
    const fetchMock = serve(
      { sources: [] },
      {
        'POST /api/sources/url': () => {
          attempts += 1;
          return attempts === 1
            ? jsonResponse(
                { title: 'One or more validation errors occurred.', status: 400, errors: { Url: ['Private network addresses are not allowed.'] } },
                400,
                'application/problem+json',
              )
            : jsonResponse(sourceDto({ id: 'n', type: 'url', title: 'Example article', status: 'pending' }), 202);
        },
      },
    );
    renderWithProviders(<SourcesPage />);

    await user.click(await screen.findByRole('button', { name: 'Add source' }));
    expect(screen.getByRole('radio', { name: 'Website' })).toHaveFocus();
    await user.click(screen.getByRole('radio', { name: 'This page only' }));
    const url = screen.getByRole('textbox', { name: 'Address' });
    await user.type(url, 'http://10.0.0.1/internal');
    await user.click(screen.getByRole('button', { name: 'Add page' }));
    expect(await screen.findByText('Private network addresses are not allowed.')).toBeInTheDocument();
    expect(url).toHaveAttribute('aria-invalid', 'true');

    await user.clear(url);
    await user.type(url, 'https://example.com/article');
    await user.click(screen.getByRole('button', { name: 'Add page' }));
    expect(await screen.findByText(/Added “Example article”/)).toBeInTheDocument();
    const body = JSON.parse((fetchMock.mock.calls.filter(([u]) => String(u).endsWith('/api/sources/url')).at(-1)![1] as RequestInit).body as string);
    expect(body).toEqual({ url: 'https://example.com/article', title: null, folderId: null, scope: 'page' });
  });

  it('refreshes the page and the stats after a source is added', async () => {
    const user = userEvent.setup();
    const db = { sources: [] as SourceDto[] };
    serve(db, {
      'POST /api/sources/text': () => {
        db.sources = [sourceDto({ id: 'notes', type: 'text', title: 'Meeting notes', status: 'pending', embeddingModel: null })];
        return jsonResponse(db.sources[0], 202);
      },
    });
    renderWithProviders(<SourcesPage />);
    expect(await screen.findByText('Add your first source')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: /^Text/ }));
    await user.type(screen.getByRole('textbox', { name: 'Title' }), 'Meeting notes');
    await user.type(screen.getByRole('textbox', { name: 'Content' }), 'Decisions of the week.');
    await user.click(screen.getByRole('button', { name: 'Add text' }));

    await expectSummary('1 source · 0 ready · 1 indexing');
    expect(within(await screen.findByRole('table')).getByText('Meeting notes')).toBeInTheDocument();
  });

  it('uploads accepted files and rejects oversized or unsupported ones before sending', async () => {
    const user = userEvent.setup({ applyAccept: false });
    const fetchMock = serve({ sources: [] }, { 'POST /api/sources/upload': () => jsonResponse(sourceDto({ id: 'u', status: 'pending' }), 202) });
    renderWithProviders(<SourcesPage />);

    await user.click(await screen.findByRole('button', { name: /^Files/ }));
    const big = new File(['x'], 'big.pdf', { type: 'application/pdf' });
    Object.defineProperty(big, 'size', { value: 26 * 1024 * 1024 });
    const files = [new File(['hello'], 'notes.md', { type: 'text/markdown' }), big, new File(['x'], 'deck.pptx')];
    await user.upload(screen.getByTestId('upload-input'), files);

    const uploads = await screen.findByRole('list', { name: 'Uploads' });
    expect(await within(uploads).findByText('Uploaded, ingestion started')).toBeInTheDocument();
    expect(within(uploads).getByText(/the limit is 25 MB/)).toBeInTheDocument();
    expect(within(uploads).getByText(/Unsupported file type/)).toBeInTheDocument();
    await waitFor(() => expect(fetchCalls(fetchMock).filter((c) => c === 'POST /api/sources/upload')).toHaveLength(1));
    const form = (fetchMock.mock.calls.find(([u]) => String(u).endsWith('/upload'))![1] as RequestInit).body as FormData;
    expect((form.get('file') as File).name).toBe('notes.md');
  });

  it('shows the details of a source with its chunks in a side sheet', async () => {
    const user = userEvent.setup();
    const chunks = Array.from({ length: 25 }, (_, i) => ({ chunkId: `c${i}`, chunkIndex: i, content: `Chunk text ${i}`, page: 1, metadata: {} }));
    serve(
      { sources: [sourceDto({ chunkCount: 25, pageCount: 12, tokenEstimate: 4200 })] },
      {
        'GET /api/sources/src-handbook/chunks': (url: URL) => {
          const skip = Number(url.searchParams.get('skip'));
          const take = Number(url.searchParams.get('take'));
          return jsonResponse(chunks.slice(skip, skip + take));
        },
      },
    );
    renderWithProviders(<SourcesPage />);
    await user.click(await screen.findByRole('button', { name: 'Details of “Employee handbook”' }));
    const dialog = await screen.findByRole('dialog', { name: 'Employee handbook' });
    expect(within(dialog).getByText('openai:text-embedding-3-small:1536')).toBeInTheDocument();
    expect(within(dialog).getByText('Pages').nextElementSibling).toHaveTextContent('12');
    expect(within(dialog).getByText('Tokens').nextElementSibling).toHaveTextContent('4,200');
    expect(await within(dialog).findByText('Chunk text 0')).toBeInTheDocument();
    expect(within(dialog).getByText('Showing 1–20 of 25')).toBeInTheDocument();
    await user.click(within(dialog).getByRole('button', { name: 'Next' }));
    expect(await within(dialog).findByText('Chunk text 20')).toBeInTheDocument();
    expect(within(dialog).getByRole('button', { name: 'Next' })).toBeDisabled();
  });

  it('opens the details from the title, which keeps a separate link to the page', async () => {
    const user = userEvent.setup();
    serve({ sources: [sourceDto({ id: 'web', type: 'url', title: 'Leave policy', url: 'https://intranet.example.com/leave', fileName: null })] });
    renderWithProviders(<SourcesPage />);
    await user.click(await screen.findByRole('button', { name: 'Leave policy' }));
    const sheet = await screen.findByRole('dialog', { name: 'Leave policy' });
    expect(within(sheet).getByRole('link', { name: /intranet\.example\.com\/leave/ })).toHaveAttribute('href', 'https://intranet.example.com/leave');
    await user.keyboard('{Escape}');
    expect(screen.queryByRole('dialog')).toBeNull();
  });

  it('opens the search test in a side sheet from the header', async () => {
    const user = userEvent.setup();
    serve({ sources: [sourceDto()] });
    renderWithProviders(<SourcesPage />);
    await screen.findByRole('table');
    expect(screen.queryByLabelText('Search query')).toBeNull();

    await user.click(screen.getByRole('button', { name: 'Test search' }));
    const sheet = screen.getByRole('dialog', { name: 'Test search' });
    expect(within(sheet).getByLabelText('Search query')).toHaveFocus();
    expect(within(sheet).getByRole('combobox', { name: 'Mode' })).toHaveTextContent('Default');
    await user.click(within(sheet).getByRole('button', { name: 'Close' }));
    expect(screen.queryByRole('dialog', { name: 'Test search' })).toBeNull();
  });
});
