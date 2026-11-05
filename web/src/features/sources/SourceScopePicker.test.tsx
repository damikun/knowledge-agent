import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { sourceDto } from '../../test/fixtures';
import { numberedSources, sourceGuid, sourcesRoutes } from '../../test/sourcesApi';
import { fetchCalls, mockFetch, renderWithProviders, requestUrls } from '../../test/utils';
import { SourceScopePicker } from './SourceScopePicker';
import type { SourceDto } from './types';

afterEach(() => {
  vi.unstubAllGlobals();
});

function Harness({ initial = [] }: { initial?: string[] }) {
  const [value, setValue] = useState<string[]>(initial);
  return (
    <>
      <SourceScopePicker value={value} onChange={setValue} />
      <output aria-label="Selected ids">{value.join(',')}</output>
    </>
  );
}

const library: SourceDto[] = [
  ...numberedSources(60),
  sourceDto({ id: 'src-handbook', title: 'Employee handbook', createdAt: '2026-09-01T00:00:00Z' }),
  sourceDto({ id: 'src-policy', title: 'Leave policy', type: 'url', url: 'https://intranet.example.com/leave', fileName: null, createdAt: '2026-09-02T00:00:00Z' }),
  sourceDto({ id: 'src-draft', title: 'Draft handbook', status: 'processing' }),
];

const searches = (fetchMock: ReturnType<typeof mockFetch>) => requestUrls(fetchMock, 'GET /api/sources');

async function openPicker(user: ReturnType<typeof userEvent.setup>, name = 'Source scope: All sources') {
  await user.click(screen.getByRole('button', { name }));
  return screen.findByRole('dialog', { name: 'Choose sources' });
}

describe('SourceScopePicker', () => {
  it('lists the 50 newest ready sources and searches the rest on the server', async () => {
    const user = userEvent.setup();
    const fetchMock = mockFetch(sourcesRoutes(() => library));
    renderWithProviders(<Harness />);
    // Nothing is loaded until the picker opens.
    expect(fetchCalls(fetchMock)).toEqual([]);

    const picker = await openPicker(user);
    expect(await within(picker).findByRole('checkbox', { name: /Source 60/ })).toBeInTheDocument();
    expect(within(picker).getAllByRole('checkbox')).toHaveLength(50);
    expect(within(picker).getByText('Showing 50 of 62 ready sources. Search to find others.')).toBeInTheDocument();
    expect(Object.fromEntries(searches(fetchMock)[0]!.searchParams)).toEqual({ status: 'ready', limit: '50' });

    await user.type(within(picker).getByRole('searchbox', { name: 'Search ready sources' }), 'handbook');
    expect(await within(picker).findByRole('checkbox', { name: /Employee handbook/ })).toBeInTheDocument();
    await waitFor(() => expect(within(picker).getAllByRole('checkbox')).toHaveLength(1));
    // One debounced request for the whole word; processing sources are not offered.
    const queries = searches(fetchMock).filter((url) => url.searchParams.has('query'));
    expect(queries.map((url) => Object.fromEntries(url.searchParams))).toEqual([{ status: 'ready', query: 'handbook', limit: '50' }]);
    expect(within(picker).queryByText('Draft handbook')).toBeNull();

    await user.clear(within(picker).getByRole('searchbox', { name: 'Search ready sources' }));
    await user.type(within(picker).getByRole('searchbox', { name: 'Search ready sources' }), 'zzz');
    expect(await within(picker).findByText('No ready sources match “zzz”.')).toBeInTheDocument();
  });

  it('keeps the labels of selected sources that later searches do not list', async () => {
    const user = userEvent.setup();
    mockFetch(sourcesRoutes(() => library));
    renderWithProviders(<Harness />);
    const picker = await openPicker(user);
    const search = within(picker).getByRole('searchbox', { name: 'Search ready sources' });

    await user.type(search, 'handbook');
    await user.click(await within(picker).findByRole('checkbox', { name: /Employee handbook/ }));
    expect(screen.getByRole('status', { name: 'Selected ids' })).toHaveTextContent('src-handbook');

    await user.clear(search);
    await user.type(search, 'leave');
    await user.click(await within(picker).findByRole('checkbox', { name: /Leave policy/ }));
    await waitFor(() => expect(within(picker).queryByRole('checkbox', { name: /Employee handbook/ })).toBeNull());

    // Both chips keep their titles although "Employee handbook" is not among the results.
    const chips = within(picker).getByRole('list', { name: 'Selected sources' });
    expect(within(chips).getAllByRole('listitem').map((chip) => chip.textContent)).toEqual(['Employee handbook', 'Leave policy']);
    expect(screen.getByRole('button', { name: 'Source scope: 2 sources' })).toBeInTheDocument();

    await user.click(within(chips).getByRole('button', { name: 'Remove Employee handbook' }));
    expect(screen.getByRole('status', { name: 'Selected ids' })).toHaveTextContent(/^src-policy$/);
    expect(screen.getByRole('button', { name: 'Source scope: Leave policy' })).toBeInTheDocument();

    await user.click(within(picker).getByRole('button', { name: 'Clear selection' }));
    expect(screen.getByRole('status', { name: 'Selected ids' })).toBeEmptyDOMElement();
    expect(screen.getByRole('button', { name: 'Source scope: All sources' })).toBeInTheDocument();
  });

  it('looks up the titles of preselected sources by id', async () => {
    const user = userEvent.setup();
    const fetchMock = mockFetch(sourcesRoutes(() => [sourceDto({ id: sourceGuid(7), title: 'Quarterly report' })]));
    renderWithProviders(<Harness initial={[sourceGuid(7)]} />);

    expect(await screen.findByRole('button', { name: 'Source scope: Quarterly report' })).toBeInTheDocument();
    expect(Object.fromEntries(searches(fetchMock)[0]!.searchParams)).toEqual({ ids: sourceGuid(7), limit: '100' });

    const picker = await openPicker(user, 'Source scope: Quarterly report');
    const chips = within(picker).getByRole('list', { name: 'Selected sources' });
    expect(within(chips).getByText('Quarterly report')).toBeInTheDocument();
  });

  it('points to the Sources page when nothing is ready yet', async () => {
    const user = userEvent.setup();
    mockFetch(sourcesRoutes(() => [sourceDto({ status: 'pending' })]));
    renderWithProviders(<Harness />);
    const picker = await openPicker(user);
    expect(await within(picker).findByText(/No ready sources yet/)).toBeInTheDocument();
    expect(within(picker).getByRole('link', { name: 'Add sources' })).toHaveAttribute('href', '/sources');
  });
});
