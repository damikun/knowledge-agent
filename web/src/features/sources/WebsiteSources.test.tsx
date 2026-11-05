import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { embeddingProfile, orchestratorProfile } from '../../test/apiFixtures';
import { sourceDto } from '../../test/fixtures';
import { sourcesRoutes } from '../../test/sourcesApi';
import { jsonResponse, mockFetch, renderWithProviders, requestUrls } from '../../test/utils';
import { SourcesPage } from './SourcesPage';
import type { SourceDto, WebsiteCrawlDto } from './types';
import { parseExcludePatterns, toAddUrlRequest } from './WebsiteForm';

afterEach(() => {
  vi.useRealTimers();
  vi.unstubAllGlobals();
});

function serve(db: { sources: SourceDto[] }, extra: Record<string, unknown> = {}) {
  return mockFetch({
    ...sourcesRoutes(() => db.sources),
    'GET /api/models/profiles': [orchestratorProfile('openai'), embeddingProfile()],
    ...extra,
  });
}

const crawl = (overrides: Partial<WebsiteCrawlDto> = {}): WebsiteCrawlDto => ({
  scope: 'section',
  maxPages: 100,
  exclude: [],
  pagesFound: 3,
  pagesDone: 3,
  pagesFailed: 0,
  ...overrides,
});

const website = (overrides: Partial<SourceDto> = {}) =>
  sourceDto({
    id: 'site',
    type: 'website',
    title: 'Example docs',
    url: 'https://docs.example.com/guide',
    site: 'docs.example.com',
    fileName: null,
    contentType: null,
    sizeBytes: null,
    chunkCount: 0,
    pageCount: 3,
    embeddingModel: null,
    crawl: crawl(),
    ...overrides,
  });

const page = (id: string, overrides: Partial<SourceDto> = {}) =>
  sourceDto({
    id,
    type: 'url',
    title: `Page ${id}`,
    url: `https://docs.example.com/guide/${id}`,
    site: 'docs.example.com',
    fileName: null,
    parentId: 'site',
    lastFetchedAt: '2026-10-21T10:00:00Z',
    ...overrides,
  });

describe('Adding a website', () => {
  it('turns the form into a request', () => {
    expect(parseExcludePatterns(' /blog/*\n\n/blog/*\n*/print ')).toEqual(['/blog/*', '*/print']);
    expect(toAddUrlRequest({ url: ' https://a.example/x ', title: '', scope: 'page', maxPages: '5', exclude: '/x' }, null)).toEqual({
      url: 'https://a.example/x',
      title: null,
      folderId: null,
      scope: 'page',
    });
    expect(toAddUrlRequest({ url: 'https://a.example/', title: 'Docs', scope: 'site', maxPages: '', exclude: '' }, 'f1')).toEqual({
      url: 'https://a.example/',
      title: 'Docs',
      folderId: 'f1',
      scope: 'site',
      maxPages: 100,
      exclude: [],
    });
  });

  it('offers the three scopes, defaults to the pages under the address and sends the limits', async () => {
    const user = userEvent.setup();
    const fetchMock = serve({ sources: [] }, { 'POST /api/sources/url': () => jsonResponse(website({ status: 'pending' }), 202) });
    renderWithProviders(<SourcesPage />);

    await user.click(await screen.findByRole('button', { name: 'Add source' }));
    const sheet = screen.getByRole('dialog', { name: 'Add a source' });
    const scopes = within(sheet).getByRole('radiogroup', { name: 'What to add' });
    expect(within(scopes).getAllByRole('radio')).toHaveLength(3);
    expect(within(scopes).getByRole('radio', { name: 'This page and the pages under it' })).toBeChecked();

    await user.type(within(sheet).getByRole('textbox', { name: 'Address' }), 'https://docs.example.com/guide');
    const maxPages = within(sheet).getByRole('spinbutton', { name: 'Max pages' });
    await user.clear(maxPages);
    await user.type(maxPages, '40');
    await user.click(within(sheet).getByText('Advanced'));
    await user.type(within(sheet).getByRole('textbox', { name: /Exclude/ }), '/guide/old/*{enter}*/print');
    await user.click(within(sheet).getByRole('button', { name: 'Add website' }));

    expect(await within(sheet).findByText(/Its pages are found and indexed in the background/)).toBeInTheDocument();
    const [, init] = fetchMock.mock.calls.find(([url]) => String(url) === '/api/sources/url')!;
    expect(JSON.parse((init as RequestInit).body as string)).toEqual({
      url: 'https://docs.example.com/guide',
      title: null,
      folderId: null,
      scope: 'section',
      maxPages: 40,
      exclude: ['/guide/old/*', '*/print'],
    });
  });

  it('hides the crawl settings for a single page', async () => {
    const user = userEvent.setup();
    serve({ sources: [] });
    renderWithProviders(<SourcesPage />);

    await user.click(await screen.findByRole('button', { name: 'Add source' }));
    const sheet = screen.getByRole('dialog', { name: 'Add a source' });
    await user.click(within(sheet).getByRole('radio', { name: 'This page only' }));

    expect(within(sheet).queryByRole('spinbutton', { name: 'Max pages' })).toBeNull();
    expect(within(sheet).queryByText('Advanced')).toBeNull();
    expect(within(sheet).getByRole('button', { name: 'Add page' })).toBeInTheDocument();
  });

  it('opens Advanced to show an error on the exclude patterns', async () => {
    const user = userEvent.setup();
    serve(
      { sources: [] },
      {
        'POST /api/sources/url': () =>
          jsonResponse(
            { title: 'One or more validation errors occurred.', status: 400, errors: { exclude: ['At most 20 exclude patterns are allowed.'] } },
            400,
            'application/problem+json',
          ),
      },
    );
    renderWithProviders(<SourcesPage />);

    await user.click(await screen.findByRole('button', { name: 'Add source' }));
    const sheet = screen.getByRole('dialog', { name: 'Add a source' });
    await user.type(within(sheet).getByRole('textbox', { name: 'Address' }), 'https://docs.example.com/');
    await user.click(within(sheet).getByRole('button', { name: 'Add website' }));

    expect(await within(sheet).findByText('At most 20 exclude patterns are allowed.')).toBeVisible();
    expect(within(sheet).getByRole('textbox', { name: /Exclude/ })).toHaveAttribute('aria-invalid', 'true');
  });
});

describe('A website source', () => {
  it('is listed without its pages and shows them with their status in its details', async () => {
    const user = userEvent.setup();
    const db = {
      sources: [
        website(),
        page('a'),
        page('shell', {
          status: 'needsbrowser',
          error: 'The page shows almost no text until JavaScript runs in a browser, so there was nothing to index.',
          chunkCount: 0,
        }),
        page('broken', { status: 'failed', error: 'The server responded with HTTP 403 (Forbidden).', chunkCount: 0 }),
      ],
    };
    const fetchMock = serve(db);
    renderWithProviders(<SourcesPage />);

    const table = await screen.findByRole('table');
    expect(within(table).getAllByRole('row')).toHaveLength(2);
    expect(within(table).getByText(/3 pages/)).toBeInTheDocument();

    await user.click(within(table).getByRole('button', { name: 'Details of “Example docs”' }));
    const dialog = await screen.findByRole('dialog', { name: 'Example docs' });
    expect(within(dialog).getByText('This page and the pages under it')).toBeInTheDocument();
    const pages = await within(dialog).findByRole('table', { name: 'Pages of the website' });
    const rows = within(pages).getAllByRole('row').slice(1);
    expect(rows).toHaveLength(3);
    expect(within(pages).getByRole('link', { name: 'Open “Page a” in a new tab' })).toHaveAttribute('href', 'https://docs.example.com/guide/a');
    expect(within(pages).getByText('Needs a browser')).toBeInTheDocument();
    expect(within(pages).getByText('The server responded with HTTP 403 (Forbidden).')).toBeInTheDocument();
    expect(within(dialog).getByText('1 page needs a browser')).toBeInTheDocument();
    expect(within(dialog).getByText(/Last crawl: 3 pages found · 3 done/)).toBeInTheDocument();
    expect(within(dialog).queryByRole('heading', { name: 'Chunks' })).toBeNull();
    expect(requestUrls(fetchMock, 'GET /api/sources').some((url) => url.searchParams.get('parent') === 'site')).toBe(true);
  });

  it('shows the crawl progress and polls its pages while it crawls', async () => {
    const user = userEvent.setup();
    const db = {
      sources: [website({ status: 'processing', stage: 'fetching', crawl: crawl({ pagesFound: 8, pagesDone: 2, pagesFailed: 1 }) }), page('a')],
    };
    const fetchMock = serve(db);
    renderWithProviders(<SourcesPage />);

    const table = await screen.findByRole('table');
    expect(within(table).getByText('Crawling 3 of 8 pages')).toBeInTheDocument();
    expect(within(table).getByRole('progressbar', { name: 'Pages crawled' })).toHaveAttribute('aria-valuenow', '3');

    await user.click(within(table).getByRole('button', { name: 'Details of “Example docs”' }));
    const dialog = await screen.findByRole('dialog', { name: 'Example docs' });
    expect(await within(dialog).findByText(/Crawling: 8 pages found · 2 done · 1 failed/)).toBeInTheDocument();

    db.sources = [...db.sources, page('b')];
    const pageRequests = () => requestUrls(fetchMock, 'GET /api/sources').filter((url) => url.searchParams.get('parent') === 'site').length;
    const before = pageRequests();
    await waitFor(() => expect(pageRequests()).toBeGreaterThan(before), { timeout: 4000 });
    expect(await within(dialog).findByText('Page b')).toBeInTheDocument();
  });
});
