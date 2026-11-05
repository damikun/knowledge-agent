import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ReactElement } from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { embeddingProfile, orchestratorProfile } from '../../test/apiFixtures';
import { sourceDto } from '../../test/fixtures';
import { filterSources, numberedSources, sourceFolder, sourceGuid, sourcesRoutes, type SourceFilterValues } from '../../test/sourcesApi';
import { dismissAllToasts, Toaster } from '../../components/ui/Toast';
import { jsonResponse, mockFetch, problemResponse, renderWithProviders as renderPage, requestUrls, type FetchHandler } from '../../test/utils';
import { SourcesPage } from './SourcesPage';
import type { SourceDto, SourceFolderDto } from './types';

afterEach(() => {
  vi.unstubAllGlobals();
  dismissAllToasts();
  localStorage.clear();
});

// Changes are confirmed in toasts, which the app shell renders.
const renderWithProviders = (ui: ReactElement, options: Parameters<typeof renderPage>[1]) =>
  renderPage(
    <>
      {ui}
      <Toaster />
    </>,
    options,
  );

interface Db {
  folders: SourceFolderDto[];
  sources: SourceDto[];
}

function library(): Db {
  return {
    folders: [sourceFolder('legal', 'Legal'), sourceFolder('contracts', 'Contracts', 'legal'), sourceFolder('archive', 'Archive')],
    sources: [
      sourceDto({ id: 'lease', title: 'Office lease', folderId: 'contracts', createdAt: '2026-10-21T10:04:00Z' }),
      sourceDto({ id: 'nda', title: 'Supplier NDA', folderId: 'contracts', createdAt: '2026-10-21T10:03:00Z' }),
      sourceDto({ id: 'policy', title: 'Privacy policy', folderId: 'legal', createdAt: '2026-10-21T10:02:00Z' }),
      sourceDto({ id: 'notes', title: 'Loose notes', folderId: null, createdAt: '2026-10-21T10:01:00Z' }),
    ],
  };
}

function body(init: RequestInit): unknown {
  return JSON.parse(init.body as string);
}

function descendants(db: Db, id: string): string[] {
  return [id, ...db.folders.filter((folder) => folder.parentId === id).flatMap((child) => descendants(db, child.id))];
}

// Serves the folders and sources of `db` and applies the folder changes the page sends.
function serve(db: Db, extra: Record<string, FetchHandler> = {}) {
  const folderRoutes: Record<string, FetchHandler> = {};
  for (const id of ['legal', 'contracts', 'archive']) {
    folderRoutes[`PATCH /api/sources/folders/${id}`] = (_url, init) => {
      const { name } = body(init) as { name: string };
      db.folders = db.folders.map((folder) => (folder.id === id ? { ...folder, name } : folder));
      return jsonResponse(db.folders.find((folder) => folder.id === id));
    };
    folderRoutes[`POST /api/sources/folders/${id}/move`] = (_url, init) => {
      const { parentId } = body(init) as { parentId: string | null };
      db.folders = db.folders.map((folder) => (folder.id === id ? { ...folder, parentId } : folder));
      return jsonResponse(db.folders.find((folder) => folder.id === id));
    };
    folderRoutes[`DELETE /api/sources/folders/${id}`] = () => {
      const deleted = new Set(descendants(db, id));
      db.folders = db.folders.filter((folder) => !deleted.has(folder.id));
      db.sources = db.sources.map((source) => (source.folderId && deleted.has(source.folderId) ? { ...source, folderId: null } : source));
      return new Response(null, { status: 204 });
    };
  }

  return mockFetch({
    ...sourcesRoutes(
      () => db.sources,
      () => db.folders,
    ),
    'GET /api/models/profiles': [orchestratorProfile('openai'), embeddingProfile()],
    'POST /api/sources/folders': (_url: URL, init: RequestInit) => {
      const request = body(init) as { name: string; parentId: string | null };
      const folder = sourceFolder(`folder-${db.folders.length + 1}`, request.name, request.parentId);
      db.folders = [...db.folders, folder];
      return jsonResponse(folder, 201);
    },
    'POST /api/sources/move': (_url: URL, init: RequestInit) => {
      const request = body(init) as { sourceIds?: string[]; folderId: string | null; matching?: SourceFilterValues };
      const ids = request.matching ? filterSources(db.sources, request.matching).map((source) => source.id) : (request.sourceIds ?? []);
      db.sources = db.sources.map((source) => (ids.includes(source.id) ? { ...source, folderId: request.folderId } : source));
      return jsonResponse({ moved: ids.length });
    },
    ...folderRoutes,
    ...extra,
  });
}

const lastFolderParam = (fetchMock: ReturnType<typeof mockFetch>) => requestUrls(fetchMock, 'GET /api/sources').at(-1)!.searchParams.get('folder');
const lastListParams = (fetchMock: ReturnType<typeof mockFetch>) =>
  Object.fromEntries(requestUrls(fetchMock, 'GET /api/sources').at(-1)!.searchParams);
const rowOf = (title: string) => screen.getByText(title).closest('tr')!;
/** A click on the row outside its title, which opens the details instead. */
const statusCellOf = (title: string) => rowOf(title).cells[2]!;
const requestBodies = (fetchMock: ReturnType<typeof mockFetch>, method: string, path: string) =>
  fetchMock.mock.calls
    .filter(([url, init]) => String(url) === path && (init as RequestInit).method === method)
    .map(([, init]) => body(init as RequestInit));

function fakeDataTransfer() {
  const data = new Map<string, string>();
  return {
    setData: (type: string, value: string) => data.set(type, value),
    getData: (type: string) => data.get(type) ?? '',
    get types() {
      return [...data.keys()];
    },
    effectAllowed: 'all',
    dropEffect: 'none',
  };
}
const rowTitles = () =>
  within(screen.getByRole('table'))
    .getAllByRole('row')
    .slice(1)
    .map((row) => row.querySelector('[data-source-title]')!.textContent);
const folders = () => screen.getByRole('navigation', { name: 'Folders' });
const breadcrumb = () => screen.getByRole('navigation', { name: 'Breadcrumb' });

/** Opens a Select by its name and chooses an option. */
async function choose(user: ReturnType<typeof userEvent.setup>, select: string, option: string | RegExp) {
  await user.click(screen.getByRole('combobox', { name: select }));
  await user.click(within(screen.getByRole('listbox', { name: select })).getByRole('option', { name: option }));
}

/** Opens a menu, of the open folder or of a row, and chooses one of its actions; a dialog may open next. */
async function chooseAction(user: ReturnType<typeof userEvent.setup>, menu: string, action: string) {
  await user.click(await screen.findByRole('button', { name: menu }));
  await user.click(within(screen.getByRole('menu', { name: menu })).getByRole('menuitem', { name: action }));
}

describe('Sources page folders', () => {
  it('lists the folders with their counts and shows one folder at a time', async () => {
    const user = userEvent.setup();
    const fetchMock = serve(library());
    renderWithProviders(<SourcesPage />, { route: '/sources', path: '/sources' });

    expect(await within(folders()).findByRole('link', { name: 'Legal, 3 sources' })).toBeInTheDocument();
    expect(within(folders()).getByRole('link', { name: 'All sources, 4 sources' })).toHaveAttribute('aria-current', 'page');
    expect(within(folders()).getByRole('link', { name: 'Unfiled, 1 source' })).toBeInTheDocument();
    expect(within(folders()).queryByRole('link', { name: /Contracts/ })).toBeNull();
    expect(screen.getAllByText('Legal / Contracts')).toHaveLength(2);

    await user.click(within(folders()).getByRole('link', { name: 'Legal, 3 sources' }));
    await waitFor(() => expect(rowTitles()).toEqual(['Privacy policy']));
    expect(lastFolderParam(fetchMock)).toBe('legal');
    expect(within(breadcrumb()).getByText('Legal')).toHaveAttribute('aria-current', 'page');
    expect(within(folders()).getByRole('link', { name: 'Legal, 3 sources' })).toHaveAttribute('aria-current', 'page');
    expect(await within(folders()).findByRole('link', { name: 'Contracts, 2 sources' })).toBeInTheDocument();

    await user.click(screen.getByRole('link', { name: 'Contracts, 2 sources including its subfolders' }));
    await waitFor(() => expect(rowTitles()).toEqual(['Office lease', 'Supplier NDA']));
    expect(lastFolderParam(fetchMock)).toBe('contracts');
    expect(within(breadcrumb()).getByRole('link', { name: 'Legal' })).toBeInTheDocument();
    expect(within(folders()).getByRole('button', { name: 'Subfolders of Legal' })).toHaveAttribute('aria-expanded', 'true');
    expect(within(folders()).getByRole('link', { name: 'Contracts, 2 sources' })).toHaveAttribute('aria-current', 'page');

    await user.type(screen.getByRole('searchbox', { name: 'Search sources' }), 'lease');
    await waitFor(() => expect(rowTitles()).toEqual(['Office lease']));
    expect(Object.fromEntries(requestUrls(fetchMock, 'GET /api/sources').at(-1)!.searchParams)).toEqual({
      query: 'lease',
      limit: '25',
      folder: 'contracts',
    });

    await user.click(within(folders()).getByRole('link', { name: 'Unfiled, 1 source' }));
    await waitFor(() => expect(lastFolderParam(fetchMock)).toBe('unfiled'));
    expect(await screen.findByText(/Nothing matches “lease” with the selected filters among the unfiled sources/)).toBeInTheDocument();

    await user.click(within(breadcrumb()).getByRole('link', { name: 'All sources' }));
    await waitFor(() => expect(rowTitles()).toEqual(['Office lease']));
    await user.click(screen.getByRole('button', { name: 'Clear' }));
    await waitFor(() => expect(rowTitles()).toHaveLength(4));
    expect(lastFolderParam(fetchMock)).toBeNull();
  });

  it('reveals the open folder in the tree but lets its parents collapse again', async () => {
    const user = userEvent.setup();
    serve(library());
    renderWithProviders(<SourcesPage />, { route: '/sources?folder=contracts', path: '/sources' });

    const toggle = await within(folders()).findByRole('button', { name: 'Subfolders of Legal' });
    await waitFor(() => expect(toggle).toHaveAttribute('aria-expanded', 'true'));
    expect(within(folders()).getByRole('link', { name: 'Contracts, 2 sources' })).toHaveAttribute('aria-current', 'page');

    await user.click(toggle);
    await waitFor(() => expect(within(folders()).queryByRole('link', { name: /Contracts/ })).toBeNull());
    expect(toggle).toHaveAttribute('aria-expanded', 'false');
  });

  it('creates a folder in the open folder and opens it', async () => {
    const user = userEvent.setup();
    const db = library();
    let attempts = 0;
    const fetchMock = serve(db, {
      'POST /api/sources/folders': (_url, init) => {
        attempts += 1;
        if (attempts === 1) {
          return problemResponse(
            { title: 'One or more validation errors occurred.', errors: { name: ["A folder named 'Contracts' already exists in 'Legal'."] } },
            400,
          );
        }
        const request = body(init) as { name: string; parentId: string | null };
        const folder = sourceFolder('drafts', request.name, request.parentId);
        db.folders = [...db.folders, folder];
        return jsonResponse(folder, 201);
      },
    });
    renderWithProviders(<SourcesPage />, { route: '/sources?folder=legal', path: '/sources' });

    await user.click(await screen.findByRole('button', { name: 'New subfolder' }));
    const dialog = screen.getByRole('dialog', { name: 'New folder' });
    expect(within(dialog).getByRole('textbox', { name: 'Name' })).toHaveFocus();
    expect(within(dialog).getByRole('radio', { name: 'Legal' })).toBeChecked();
    expect(within(dialog).getByRole('radio', { name: 'Contracts' })).toBeEnabled();

    await user.type(within(dialog).getByRole('textbox', { name: 'Name' }), 'Contracts');
    await user.click(within(dialog).getByRole('button', { name: 'Create folder' }));
    expect(await within(dialog).findByText("A folder named 'Contracts' already exists in 'Legal'.")).toBeInTheDocument();
    expect(within(dialog).getByRole('textbox', { name: 'Name' })).toHaveAttribute('aria-invalid', 'true');

    await user.clear(within(dialog).getByRole('textbox', { name: 'Name' }));
    await user.type(within(dialog).getByRole('textbox', { name: 'Name' }), 'Drafts');
    await user.click(within(dialog).getByRole('button', { name: 'Create folder' }));

    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
    expect(await within(breadcrumb()).findByText('Drafts')).toHaveAttribute('aria-current', 'page');
    expect(await screen.findByText('This folder is empty')).toBeInTheDocument();
    expect(lastFolderParam(fetchMock)).toBe('drafts');
    const request = fetchMock.mock.calls
      .filter(([url, init]) => String(url) === '/api/sources/folders' && (init as RequestInit).method === 'POST')
      .at(-1)!;
    expect(body(request[1] as RequestInit)).toEqual({ name: 'Drafts', parentId: 'legal' });
  });

  it('renames, moves and deletes the open folder', async () => {
    const user = userEvent.setup();
    const db = library();
    const fetchMock = serve(db);
    renderWithProviders(<SourcesPage />, { route: '/sources?folder=legal', path: '/sources' });

    await chooseAction(user, 'Folder actions', 'Rename');
    const rename = await screen.findByRole('dialog', { name: 'Rename “Legal”' });
    expect(within(rename).getByRole('textbox', { name: 'Name' })).toHaveFocus();
    await user.clear(within(rename).getByRole('textbox', { name: 'Name' }));
    await user.type(within(rename).getByRole('textbox', { name: 'Name' }), 'Legal and compliance');
    await user.click(within(rename).getByRole('button', { name: 'Rename' }));
    expect(await within(breadcrumb()).findByText('Legal and compliance')).toBeInTheDocument();

    await chooseAction(user, 'Folder actions', 'Move');
    const move = await screen.findByRole('dialog', { name: 'Move “Legal and compliance”' });
    expect(within(move).getByRole('radio', { name: /Top level/ })).toBeDisabled();
    expect(within(move).getByRole('radio', { name: /Contracts/ })).toBeDisabled();
    await user.click(within(move).getByRole('radio', { name: 'Archive' }));
    await user.click(within(move).getByRole('button', { name: 'Move folder' }));
    expect(await within(breadcrumb()).findByRole('link', { name: 'Archive' })).toBeInTheDocument();

    await chooseAction(user, 'Folder actions', 'Delete');
    const confirm = await screen.findByRole('dialog', { name: 'Delete folder?' });
    expect(confirm).toHaveTextContent(
      '“Legal and compliance” and its 1 subfolder will be deleted. The 3 sources inside stay in the library, unfiled.',
    );
    await user.click(within(confirm).getByRole('button', { name: 'Delete folder' }));

    expect(await screen.findByText('Deleted “Legal and compliance”. Its sources are in Unfiled now.')).toBeInTheDocument();
    expect(await within(breadcrumb()).findByText('Archive')).toHaveAttribute('aria-current', 'page');
    await waitFor(() => expect(within(folders()).getByRole('link', { name: 'Unfiled, 4 sources' })).toBeInTheDocument());
    const calls = fetchMock.mock.calls.map(([url, init]) => `${(init as RequestInit).method ?? 'GET'} ${String(url)}`);
    expect(calls).toContain('PATCH /api/sources/folders/legal');
    expect(calls).toContain('POST /api/sources/folders/legal/move');
    expect(calls).toContain('DELETE /api/sources/folders/legal');
  });

  it('moves the selected sources into a folder', async () => {
    const user = userEvent.setup();
    const db = library();
    const fetchMock = serve(db);
    renderWithProviders(<SourcesPage />, { route: '/sources', path: '/sources' });

    await user.click(await screen.findByRole('checkbox', { name: 'Select “Privacy policy”' }));
    await user.click(screen.getByRole('checkbox', { name: 'Select “Loose notes”' }));
    expect(screen.getByRole('checkbox', { name: 'Select every source on this page' })).toHaveProperty('indeterminate', true);
    expect(screen.getByText('2 sources selected')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Move to folder' }));
    const dialog = screen.getByRole('dialog', { name: 'Move 2 sources' });
    await user.click(within(dialog).getByRole('radio', { name: 'Archive' }));
    await user.click(within(dialog).getByRole('button', { name: 'Move' }));

    expect(await screen.findByText('Moved 2 sources to “Archive”.')).toBeInTheDocument();
    expect(screen.queryByText('2 sources selected')).toBeNull();
    const move = fetchMock.mock.calls.find(([url]) => String(url) === '/api/sources/move')!;
    expect(body(move[1] as RequestInit)).toEqual({ sourceIds: ['policy', 'notes'], folderId: 'archive' });
    await waitFor(() => expect(within(folders()).getByRole('link', { name: 'Archive, 2 sources' })).toBeInTheDocument());

    await user.click(screen.getByRole('checkbox', { name: 'Select every source on this page' }));
    expect(screen.getByText('4 sources selected')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Clear selection' }));
    expect(screen.queryByText(/selected$/)).toBeNull();
  });

  it('moves one source from its row and can take sources out of every folder', async () => {
    const user = userEvent.setup();
    const db = library();
    const fetchMock = serve(db);
    renderWithProviders(<SourcesPage />, { route: '/sources?folder=contracts', path: '/sources' });

    await chooseAction(user, 'Actions for “Office lease”', 'Move to folder');
    const dialog = await screen.findByRole('dialog', { name: 'Move “Office lease”' });
    expect(within(dialog).getByRole('radio', { name: /Contracts/ })).toBeDisabled();
    await user.click(within(dialog).getByRole('radio', { name: 'Unfiled (no folder)' }));
    await user.click(within(dialog).getByRole('button', { name: 'Move' }));

    expect(await screen.findByText('Moved 1 source out of its folder.')).toBeInTheDocument();
    await waitFor(() => expect(rowTitles()).toEqual(['Supplier NDA']));
    const move = fetchMock.mock.calls.find(([url]) => String(url) === '/api/sources/move')!;
    expect(body(move[1] as RequestInit)).toEqual({ sourceIds: ['lease'], folderId: null });
  });

  it('moves sources dragged onto a folder', async () => {
    const db = library();
    const fetchMock = serve(db);
    renderWithProviders(<SourcesPage />, { route: '/sources?folder=contracts', path: '/sources' });
    const user = userEvent.setup();
    await user.click(await screen.findByRole('checkbox', { name: 'Select “Office lease”' }));
    await user.click(screen.getByRole('checkbox', { name: 'Select “Supplier NDA”' }));

    const data = new Map<string, string>();
    const dataTransfer = {
      setData: (type: string, value: string) => data.set(type, value),
      getData: (type: string) => data.get(type) ?? '',
      get types() {
        return [...data.keys()];
      },
      effectAllowed: 'all',
      dropEffect: 'none',
    };
    fireEvent.dragStart(screen.getByText('Office lease').closest('tr')!, { dataTransfer });
    const archive = within(folders()).getByRole('link', { name: /^Archive/ }).parentElement!;
    fireEvent.dragOver(archive, { dataTransfer });
    fireEvent.drop(archive, { dataTransfer });

    expect(await screen.findByText('Moved 2 sources to “Archive”.')).toBeInTheDocument();
    const move = fetchMock.mock.calls.find(([url]) => String(url) === '/api/sources/move')!;
    expect(body(move[1] as RequestInit)).toEqual({ sourceIds: ['lease', 'nda'], folderId: 'archive' });
    expect(await screen.findByText('This folder is empty')).toBeInTheDocument();
    await user.click(within(folders()).getByRole('link', { name: 'Archive, 2 sources' }));
    await waitFor(() => expect(rowTitles()).toEqual(['Office lease', 'Supplier NDA']));
  });

  it('hides the folder panel and remembers it, and remembers which folders are expanded', async () => {
    const user = userEvent.setup();
    serve(library());
    const { unmount } = renderWithProviders(<SourcesPage />, { route: '/sources', path: '/sources' });

    const hide = await screen.findByRole('button', { name: 'Hide folders' });
    expect(hide).toHaveAttribute('aria-expanded', 'true');
    expect(hide).toHaveAttribute('aria-controls', 'source-folders');
    await user.click(hide);
    expect(screen.getByRole('button', { name: 'Show folders' })).toHaveAttribute('aria-expanded', 'false');

    const legal = await within(folders()).findByRole('button', { name: 'Subfolders of Legal' });
    expect(legal).toHaveAttribute('aria-expanded', 'false');
    await user.click(legal);
    expect(await within(folders()).findByRole('link', { name: 'Contracts, 2 sources' })).toBeInTheDocument();
    unmount();

    renderWithProviders(<SourcesPage />, { route: '/sources', path: '/sources' });
    expect(await screen.findByRole('button', { name: 'Show folders' })).toBeInTheDocument();
    expect(await within(folders()).findByRole('button', { name: 'Subfolders of Legal' })).toHaveAttribute('aria-expanded', 'true');
    expect(within(folders()).getByRole('link', { name: 'Contracts, 2 sources' })).toBeInTheDocument();

    // Collapsing is remembered too.
    await user.click(within(folders()).getByRole('button', { name: 'Subfolders of Legal' }));
    expect(within(folders()).queryByRole('link', { name: /Contracts/ })).toBeNull();
    expect(JSON.parse(localStorage.getItem('ka.sources.expandedFolders')!)).toEqual([]);
  });

  it('points to the subfolders when a folder holds no sources itself', async () => {
    const db = library();
    db.sources = db.sources.map((source) => (source.id === 'policy' ? { ...source, folderId: null } : source));
    serve(db);
    renderWithProviders(<SourcesPage />, { route: '/sources?folder=legal', path: '/sources' });

    expect(await screen.findByText('No sources directly in this folder')).toBeInTheDocument();
    expect(screen.getByText(/Its subfolders hold 2 sources/)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Contracts, 2 sources including its subfolders' })).toBeInTheDocument();
  });

  it('selects rows like files: a click picks one, Cmd or Ctrl adds, Shift picks a range and Escape clears', async () => {
    const user = userEvent.setup();
    serve({ folders: [], sources: numberedSources(6) });
    renderWithProviders(<SourcesPage />, { route: '/sources', path: '/sources' });
    const checkbox = (n: number) => screen.getByRole('checkbox', { name: `Select “Source ${n}”` });

    await screen.findByRole('table');
    await user.click(statusCellOf('Source 5'));
    expect(checkbox(5)).toBeChecked();
    await user.click(statusCellOf('Source 3'));
    expect(checkbox(5)).not.toBeChecked();
    expect(checkbox(3)).toBeChecked();

    await user.keyboard('{Meta>}');
    await user.click(statusCellOf('Source 1'));
    await user.keyboard('{/Meta}');
    expect(screen.getByText('2 sources selected')).toBeInTheDocument();

    await user.keyboard('{Shift>}');
    await user.click(statusCellOf('Source 4'));
    await user.keyboard('{/Shift}');
    expect([6, 5, 4, 3, 2, 1].map((n) => (checkbox(n) as HTMLInputElement).checked)).toEqual([false, false, true, true, true, true]);

    await user.click(checkbox(4));
    expect(screen.getByText('3 sources selected')).toBeInTheDocument();
    await user.keyboard('{Meta>}');
    await user.click(statusCellOf('Source 6'));
    await user.keyboard('{/Meta}');
    expect(screen.getByText('4 sources selected')).toBeInTheDocument();
    await user.keyboard('{Escape}');
    expect(screen.queryByText(/sources? selected/)).toBeNull();
  });

  it('selects every source the filters match across pages and moves them in one request', async () => {
    const user = userEvent.setup();
    const db: Db = { folders: [sourceFolder('archive', 'Archive')], sources: numberedSources(30) };
    const fetchMock = serve(db);
    renderWithProviders(<SourcesPage />, { route: '/sources?folder=unfiled', path: '/sources' });

    await user.click(await screen.findByRole('checkbox', { name: 'Select every source on this page' }));
    expect(screen.getByText('25 sources selected')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Select all 30 sources' }));
    expect(screen.getByText('All 30 sources selected')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Move to folder' }));
    const dialog = screen.getByRole('dialog', { name: 'Move 30 sources' });
    expect(within(dialog).getByRole('radio', { name: /Unfiled \(no folder\)/ })).toBeDisabled();
    await user.click(within(dialog).getByRole('radio', { name: 'Archive' }));
    await user.click(within(dialog).getByRole('button', { name: 'Move' }));

    expect(await screen.findByText('Moved 30 sources to “Archive”.')).toBeInTheDocument();
    expect(requestBodies(fetchMock, 'POST', '/api/sources/move')).toEqual([
      { matching: { query: null, type: null, status: null, folder: 'unfiled', site: null }, folderId: 'archive' },
    ]);
    expect(await screen.findByText('Nothing unfiled')).toBeInTheDocument();
  });

  it('nests a dragged folder in another one and refuses drops into its own subfolders', async () => {
    const db = library();
    const fetchMock = serve(db);
    renderWithProviders(<SourcesPage />, { route: '/sources?folder=contracts', path: '/sources' });
    const legalRow = (await within(folders()).findByRole('link', { name: 'Legal, 3 sources' })).parentElement!;
    const contractsRow = within(folders()).getByRole('link', { name: 'Contracts, 2 sources' }).parentElement!;
    const archiveRow = within(folders()).getByRole('link', { name: 'Archive, 0 sources' }).parentElement!;

    const intoChild = fakeDataTransfer();
    fireEvent.dragStart(legalRow, { dataTransfer: intoChild });
    expect(fireEvent.dragOver(contractsRow, { dataTransfer: intoChild })).toBe(true);
    fireEvent.drop(contractsRow, { dataTransfer: intoChild });
    fireEvent.dragEnd(legalRow, { dataTransfer: intoChild });
    expect(requestBodies(fetchMock, 'POST', '/api/sources/folders/legal/move')).toEqual([]);

    const intoArchive = fakeDataTransfer();
    fireEvent.dragStart(legalRow, { dataTransfer: intoArchive });
    expect(fireEvent.dragOver(archiveRow, { dataTransfer: intoArchive })).toBe(false);
    fireEvent.drop(archiveRow, { dataTransfer: intoArchive });

    expect(await screen.findByText('Moved “Legal” into “Archive”.')).toBeInTheDocument();
    expect(requestBodies(fetchMock, 'POST', '/api/sources/folders/legal/move')).toEqual([{ parentId: 'archive' }]);
    expect(await within(breadcrumb()).findByRole('link', { name: 'Archive' })).toBeInTheDocument();
  });

  it('moves a dragged folder to the top level when it is dropped on the heading', async () => {
    const db = library();
    const fetchMock = serve(db);
    renderWithProviders(<SourcesPage />, { route: '/sources?folder=contracts', path: '/sources' });
    const contractsRow = (await within(folders()).findByRole('link', { name: 'Contracts, 2 sources' })).parentElement!;
    const heading = within(folders()).getByRole('heading', { name: 'Folders' }).parentElement!;

    const dataTransfer = fakeDataTransfer();
    fireEvent.dragStart(contractsRow, { dataTransfer });
    fireEvent.dragOver(heading, { dataTransfer });
    expect(within(folders()).getByRole('heading', { name: 'Move to the top level' })).toBeInTheDocument();
    fireEvent.drop(heading, { dataTransfer });

    expect(await screen.findByText('Moved “Contracts” to the top level.')).toBeInTheDocument();
    expect(requestBodies(fetchMock, 'POST', '/api/sources/folders/contracts/move')).toEqual([{ parentId: null }]);
  });

  it('filters by site from a row or from the site menu', async () => {
    const user = userEvent.setup();
    const page = (n: number, site: string) =>
      sourceDto({
        id: sourceGuid(n),
        type: 'url',
        title: `Page ${n}`,
        url: `https://${site}/${n}`,
        site,
        fileName: null,
        createdAt: `2026-10-2${n}T08:00:00Z`,
      });
    const fetchMock = serve({
      folders: [],
      sources: [page(1, 'docs.python.org'), page(2, 'docs.python.org'), page(3, 'peps.python.org'), sourceDto()],
    });
    renderWithProviders(<SourcesPage />, { route: '/sources', path: '/sources' });

    const siteMenu = await screen.findByRole('combobox', { name: 'Site' });
    await user.click(siteMenu);
    await waitFor(() =>
      expect(
        within(screen.getByRole('listbox', { name: 'Site' }))
          .getAllByRole('option')
          .map((option) => option.textContent),
      ).toEqual(['All sites', 'docs.python.org2', 'peps.python.org1']),
    );
    await user.keyboard('{Escape}');

    await user.click(within(rowOf('Page 3')).getByRole('button', { name: 'Show only pages from peps.python.org' }));
    await waitFor(() => expect(rowTitles()).toEqual(['Page 3']));
    expect(lastListParams(fetchMock)).toEqual({ site: 'peps.python.org', limit: '25' });
    expect(siteMenu).toHaveTextContent('peps.python.org');

    await choose(user, 'Site', /^docs\.python\.org/);
    await waitFor(() => expect(rowTitles()).toEqual(['Page 2', 'Page 1']));
    await choose(user, 'Site', 'All sites');
    await waitFor(() => expect(rowTitles()).toHaveLength(4));
    expect(lastListParams(fetchMock)).toEqual({ limit: '25' });
  });

  it('shows more sources per page on request and remembers the choice', async () => {
    const user = userEvent.setup();
    const fetchMock = serve({ folders: [], sources: numberedSources(60) });
    const { unmount } = renderWithProviders(<SourcesPage />, { route: '/sources', path: '/sources' });

    await screen.findByRole('combobox', { name: 'Sources per page' });
    await choose(user, 'Sources per page', '50');
    expect(await screen.findByText('Showing 1–50 of 60')).toBeInTheDocument();
    expect(lastListParams(fetchMock)).toEqual({ limit: '50' });
    unmount();

    renderWithProviders(<SourcesPage />, { route: '/sources', path: '/sources' });
    expect(await screen.findByText('Showing 1–50 of 60')).toBeInTheDocument();
  });

  it('files new sources in the open folder', async () => {
    const user = userEvent.setup();
    const fetchMock = serve(library(), {
      'POST /api/sources/url': () =>
        jsonResponse(sourceDto({ id: 'page', type: 'url', title: 'Example', status: 'pending', folderId: 'contracts' }), 202),
    });
    renderWithProviders(<SourcesPage />, { route: '/sources?folder=contracts', path: '/sources' });

    await within(breadcrumb()).findByText('Contracts');
    await user.click(screen.getByRole('button', { name: 'Add source' }));
    const sheet = screen.getByRole('dialog', { name: 'Add a source' });
    expect(sheet).toHaveAccessibleDescription('New sources go into Legal / Contracts.');
    await user.type(within(sheet).getByRole('textbox', { name: 'Address' }), 'https://example.com/docs');
    await user.click(screen.getByRole('button', { name: 'Add website' }));

    await screen.findByText(/Added “Example”/);
    const request = fetchMock.mock.calls.find(([url]) => String(url) === '/api/sources/url')!;
    expect(body(request[1] as RequestInit)).toEqual({
      url: 'https://example.com/docs',
      title: null,
      folderId: 'contracts',
      scope: 'section',
      maxPages: 100,
      exclude: [],
    });
  });

  it('says so when the folder in the address no longer exists', async () => {
    const user = userEvent.setup();
    const fetchMock = serve(library());
    renderWithProviders(<SourcesPage />, { route: '/sources?folder=gone', path: '/sources' });

    expect(await screen.findByText('Folder not found', { selector: 'div' })).toBeInTheDocument();
    expect(within(breadcrumb()).getByText('Folder not found')).toHaveAttribute('aria-current', 'page');
    // New sources cannot go into a folder that is gone.
    await user.click(screen.getByRole('button', { name: 'Add source' }));
    expect(screen.getByRole('dialog', { name: 'Add a source' })).not.toHaveAccessibleDescription(/New sources go into/);
    await user.keyboard('{Escape}');

    await user.click(screen.getByRole('button', { name: 'Show all sources' }));
    await waitFor(() => expect(rowTitles()).toHaveLength(4));
    expect(lastFolderParam(fetchMock)).toBeNull();
  });

  it('shows the Internal folder locked, with its note and without rename, move or delete', async () => {
    const user = userEvent.setup();
    const db = library();
    db.folders = [...db.folders, { ...sourceFolder('internal', 'Internal'), isInternal: true }, sourceFolder('hr', 'HR', 'internal')];
    db.sources = [...db.sources, sourceDto({ id: 'bands', title: 'Salary bands', folderId: 'hr' })];
    serve(db);
    renderWithProviders(<SourcesPage />, { route: '/sources?folder=internal', path: '/sources' });

    const link = await within(folders()).findByRole('link', { name: /^Internal/ });
    expect(link).toHaveAccessibleDescription('Only the help assistant searches these, for your team.');
    expect(link.closest('[draggable]')).toHaveAttribute('draggable', 'false');
    expect(await screen.findByTestId('internal-folder-note')).toHaveTextContent('Only the help assistant searches these, for your team.');
    expect(screen.queryByRole('button', { name: 'Folder actions' })).toBeNull();
    // Inside it, folders behave as anywhere else.
    expect(screen.getByRole('button', { name: 'New subfolder' })).toBeEnabled();

    await user.click(within(folders()).getByRole('link', { name: /^Legal/ }));
    expect(await screen.findByRole('button', { name: 'Folder actions' })).toBeInTheDocument();
    expect(screen.queryByTestId('internal-folder-note')).toBeNull();
  });
});
