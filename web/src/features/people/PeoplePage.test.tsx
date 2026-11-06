import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Route } from 'react-router';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { supportUser } from '../../test/apiFixtures';
import { Toaster } from '../../components/ui/Toast';
import { jsonResponse, mockFetch, problemResponse, renderWithProviders, requestUrls } from '../../test/utils';
import { widgetDto, widgetGuid, widgetsRoutes } from '../../test/widgetsApi';
import { PeoplePage } from './PeoplePage';
import { PersonFilter } from './PersonFilter';
import { PersonPage } from './PersonPage';
import { ADA_ID, CONVERSATION_ID, ada, visitor, adaProfile, registry, SEGMENT_ID, proSegment, conversationsPage } from '../../test/peopleApi';

afterEach(() => {
  vi.unstubAllGlobals();
});

function decoded(url: URL | undefined): unknown {
  const filter = url?.searchParams.get('filter');
  if (!filter) {
    return null;
  }
  const base64 = filter.replace(/-/g, '+').replace(/_/g, '/');
  return JSON.parse(atob(base64.padEnd(base64.length + ((4 - (base64.length % 4)) % 4), '=')));
}

describe('PeoplePage', () => {
  it('lists people with their sign-in, widgets and conversations, searches them and picks a built-in segment', async () => {
    const fetchMock = mockFetch({
      'GET /api/people': { items: [ada, visitor], nextCursor: null },
      'GET /api/people/count': { count: 2 },
      'GET /api/people/attributes': registry,
      'GET /api/segments': [proSegment],
      ...widgetsRoutes(() => [widgetDto(), widgetDto({ id: widgetGuid(2), name: 'Docs help' })]),
    });
    renderWithProviders(<PeoplePage />, { route: '/contacts' });

    const table = await screen.findByRole('table', { name: 'Contacts' });
    const rows = within(table).getAllByRole('row').slice(1);
    expect(rows).toHaveLength(2);
    expect(within(rows[0]!).getByRole('link', { name: 'Ada Lovelace' })).toHaveAttribute('href', `/contacts/${ADA_ID}`);
    expect(rows[0]).toHaveTextContent('Signed in');
    expect(rows[0]).toHaveTextContent('ada@example.com');
    expect(rows[0]).toHaveTextContent('Shop help');
    expect(rows[1]).toHaveTextContent('Visitor 4F2A');
    expect(rows[1]).not.toHaveTextContent('Signed in');
    expect(await screen.findByText(/2 contacts/)).toBeInTheDocument();

    const segments = screen.getByRole('navigation', { name: 'Segments' });
    expect(await within(segments).findByRole('button', { name: /^All contacts\s*2$/ })).toHaveAttribute('aria-current', 'true');
    expect(within(segments).getByRole('button', { name: /^New this week\s*2$/ })).toBeInTheDocument();
    expect(await within(segments).findByRole('button', { name: /Pro customers\s*120/ })).toBeInTheDocument();
    const builtInCounts = requestUrls(fetchMock, 'GET /api/people/count').map((url) => decoded(url));
    expect(builtInCounts).toEqual(expect.arrayContaining([null, [{ attr: 'identified', op: 'true' }], [{ attr: 'returning', op: 'true' }]]));

    await userEvent.type(screen.getByRole('searchbox', { name: 'Search contacts' }), 'ada');
    await waitFor(() => expect(requestUrls(fetchMock, 'GET /api/people').some((url) => url.searchParams.get('q') === 'ada')).toBe(true));

    await userEvent.click(within(segments).getByRole('button', { name: /^Identified/ }));
    await waitFor(() => expect(decoded(requestUrls(fetchMock, 'GET /api/people').at(-1))).toEqual([{ attr: 'identified', op: 'true' }]));
    await userEvent.click(within(segments).getByRole('button', { name: /^Pro customers/ }));
    await waitFor(() => expect(requestUrls(fetchMock, 'GET /api/people').at(-1)?.searchParams.get('segmentId')).toBe(SEGMENT_ID));
    expect(requestUrls(fetchMock, 'GET /api/people/count').at(-1)?.searchParams.get('segmentId')).toBe(SEGMENT_ID);
  });

  it('builds a filter from typed conditions, shows it as chips and saves it as a segment', async () => {
    const fetchMock = mockFetch({
      'GET /api/people': { items: [ada], nextCursor: null },
      'GET /api/people/count': { count: 1 },
      'GET /api/people/attributes': registry,
      'GET /api/people/attributes/plan/values': [{ value: 'pro', contacts: 120 }],
      'GET /api/segments': [],
      'POST /api/segments': (_url: URL, init: RequestInit) =>
        jsonResponse({ ...proSegment, ...(JSON.parse(String(init.body)) as object), id: SEGMENT_ID }, 201),
      ...widgetsRoutes(() => [widgetDto()]),
    });
    renderWithProviders(<PeoplePage />, { route: '/contacts' });
    await screen.findByRole('table', { name: 'Contacts' });
    expect(screen.queryByRole('button', { name: 'Save as segment' })).not.toBeInTheDocument();
    expect(await screen.findByText('Save a filtered list to see it here.')).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'New segment' }));
    const picker = await screen.findByRole('dialog', { name: 'Filter contacts' });
    expect(within(picker).getByRole('group', { name: 'Built-in' })).toHaveTextContent('Identified');
    await userEvent.type(within(picker).getByRole('searchbox', { name: 'Find an attribute' }), 'sea');
    expect(within(picker).queryByRole('button', { name: /Plan/ })).not.toBeInTheDocument();
    await userEvent.click(within(picker).getByRole('button', { name: /Seats/ }));
    await userEvent.click(within(picker).getByRole('combobox', { name: 'Condition' }));
    await userEvent.click(await screen.findByRole('option', { name: 'is between' }));
    await userEvent.type(within(picker).getByRole('spinbutton', { name: 'From' }), '5');
    await userEvent.type(within(picker).getByRole('spinbutton', { name: 'To' }), '10');
    await userEvent.click(within(picker).getByRole('button', { name: 'Apply' }));

    await waitFor(() =>
      expect(decoded(requestUrls(fetchMock, 'GET /api/people').at(-1))).toEqual([{ attr: 'seats', op: 'between', value: [5, 10] }]),
    );
    expect(screen.getByRole('button', { name: 'Edit filter: Seats is between 5 and 10' })).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Filter' }));
    const second = await screen.findByRole('dialog', { name: 'Filter contacts' });
    await userEvent.click(within(second).getByRole('button', { name: /Plan/ }));
    await userEvent.type(await within(second).findByRole('combobox', { name: 'Value' }), 'pro');
    await userEvent.click(within(second).getByRole('button', { name: 'Apply' }));
    await waitFor(() => expect(decoded(requestUrls(fetchMock, 'GET /api/people').at(-1))).toHaveLength(2));

    await userEvent.click(screen.getByRole('button', { name: 'New segment' }));
    expect(await screen.findByRole('dialog', { name: 'Save as segment' })).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Save as segment' })).not.toBeInTheDocument());

    await userEvent.click(screen.getByRole('button', { name: 'Save as segment' }));
    const dialog = await screen.findByRole('dialog', { name: 'Save as segment' });
    await userEvent.type(within(dialog).getByRole('textbox', { name: 'Name' }), 'Pro teams');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Save segment' }));
    await waitFor(() => expect(requestUrls(fetchMock, 'POST /api/segments')).toHaveLength(1));
    const call = fetchMock.mock.calls.find(
      ([input, init]) => String(input).includes('/api/segments') && (init as RequestInit | undefined)?.method === 'POST',
    );
    expect(JSON.parse(String((call![1] as RequestInit).body))).toMatchObject({
      name: 'Pro teams',
      shared: true,
      filter: [
        { attr: 'seats', op: 'between', value: [5, 10] },
        { attr: 'plan', op: 'is', value: 'pro' },
      ],
    });
    await waitFor(() => expect(requestUrls(fetchMock, 'GET /api/people').at(-1)?.searchParams.get('segmentId')).toBe(SEGMENT_ID));

    await userEvent.click(screen.getByRole('button', { name: 'Columns' }));
    await userEvent.click(within(await screen.findByRole('dialog', { name: 'Columns' })).getByRole('checkbox', { name: 'Seats' }));
    const table = screen.getByRole('table', { name: 'Contacts' });
    expect(within(table).getByRole('columnheader', { name: 'Seats' })).toBeInTheDocument();
    expect(within(table).getAllByRole('row')[1]).toHaveTextContent('5');
  });

  it('manages the attribute registry on its own tab', async () => {
    const fetchMock = mockFetch({
      'GET /api/people/attributes': registry,
      'PATCH /api/people/attributes/plan': (_url: URL, init: RequestInit) =>
        jsonResponse({ ...registry.items[0], ...(JSON.parse(String(init.body)) as object) }),
      'DELETE /api/people/attributes/seats': { contacts: 80 },
    });
    renderWithProviders(<PeoplePage />, { route: '/contacts?view=attributes' });

    expect(screen.getByRole('radio', { name: 'Attributes' })).toBeChecked();
    const table = await screen.findByRole('table', { name: 'Attributes' });
    expect(within(table).getAllByRole('row')).toHaveLength(3);
    expect(screen.getByText('2 attributes')).toBeInTheDocument();
    await userEvent.type(screen.getByRole('searchbox', { name: 'Search attributes' }), 'sea');
    expect(within(table).getAllByRole('row')).toHaveLength(2);
    await userEvent.clear(screen.getByRole('searchbox', { name: 'Search attributes' }));
    await userEvent.click(within(table).getByRole('switch', { name: 'Give Plan to the agent' }));
    await waitFor(() => expect(requestUrls(fetchMock, 'PATCH /api/people/attributes/plan')).toHaveLength(1));
    const label = within(table).getByRole('textbox', { name: 'Label of plan' });
    await userEvent.clear(label);
    await userEvent.type(label, 'Pricing plan{Enter}');
    await waitFor(() => expect(requestUrls(fetchMock, 'PATCH /api/people/attributes/plan')).toHaveLength(2));
    const bodies = fetchMock.mock.calls
      .filter(([, init]) => (init as RequestInit | undefined)?.method === 'PATCH')
      .map(([, init]) => JSON.parse(String((init as RequestInit).body)) as unknown);
    expect(bodies).toEqual([{ exposeToAgent: true }, { label: 'Pricing plan' }]);

    await userEvent.click(within(table).getByRole('button', { name: 'Delete Seats' }));
    const confirm = await screen.findByRole('dialog', { name: 'Delete Seats?' });
    expect(confirm).toHaveTextContent('80 contacts');
    await userEvent.click(within(confirm).getByRole('button', { name: 'Delete attribute' }));
    await waitFor(() => expect(requestUrls(fetchMock, 'DELETE /api/people/attributes/seats')).toHaveLength(1));
  });

  it('explains an empty list', async () => {
    mockFetch({ 'GET /api/people': { items: [], nextCursor: null }, 'GET /api/auth/me': supportUser });
    renderWithProviders(<PeoplePage />, { route: '/contacts' });

    expect(await screen.findByText('No people yet')).toBeInTheDocument();
    expect(screen.queryByRole('combobox', { name: 'Widget' })).not.toBeInTheDocument();
  });
});

describe('PersonPage', () => {
  it('shows the person, their numbers, attributes with their source and conversations linking to the Inbox', async () => {
    const fetchMock = mockFetch({ [`GET /api/people/${ADA_ID}`]: adaProfile, 'GET /api/conversations/search': conversationsPage });
    renderWithProviders(<PersonPage />, { route: `/contacts/${ADA_ID}`, path: '/contacts/:personId' });

    expect(await screen.findByRole('heading', { level: 1, name: 'Ada Lovelace' })).toBeInTheDocument();
    expect(screen.getByText('4.5/5')).toBeInTheDocument();
    expect(screen.getByText('user-42')).toBeInTheDocument();
    expect(screen.getByText('Plan')).toBeInTheDocument();
    expect(screen.getByText('Server')).toHaveAttribute('title', expect.stringContaining('signed with the identity secret'));
    expect(screen.getByText('Browser')).toHaveAttribute('title', expect.stringContaining('not verified'));
    expect(screen.queryByText('optional')).not.toBeInTheDocument();
    expect(await screen.findByRole('link', { name: /Return an order/ })).toHaveAttribute('href', `/inbox?person=${ADA_ID}&c=${CONVERSATION_ID}`);
    expect(screen.getByRole('link', { name: 'Open in Inbox' })).toHaveAttribute('href', `/inbox?person=${ADA_ID}`);
    expect(requestUrls(fetchMock, 'GET /api/conversations/search')[0]?.searchParams.get('contactId')).toBe(ADA_ID);
  });

  it('edits one row at a time: Enter saves with an Undo, Esc cancels, and the website warning shows for signed-in users', async () => {
    let current = adaProfile;
    const fetchMock = mockFetch({
      [`GET /api/people/${ADA_ID}`]: () => jsonResponse(current),
      'GET /api/conversations/search': conversationsPage,
      [`PATCH /api/people/${ADA_ID}`]: (_url: URL, init: RequestInit) => {
        const body = JSON.parse(String(init.body)) as { name?: string };
        current = { ...current, ...(body.name !== undefined ? { name: body.name, savedName: body.name } : {}) };
        return jsonResponse(current);
      },
    });
    renderWithProviders(
      <>
        <PersonPage />
        <Toaster />
      </>,
      { route: `/contacts/${ADA_ID}`, path: '/contacts/:personId' },
    );
    await screen.findByRole('heading', { level: 1, name: 'Ada Lovelace' });

    expect(screen.queryByRole('textbox', { name: 'Name' })).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Edit Name' }));
    const input = screen.getByRole('textbox', { name: 'Name' });
    expect(input).toHaveFocus();
    expect(screen.getByText(/next sign-in may replace your edit/)).toBeInTheDocument();
    await userEvent.type(input, 'X{Escape}');
    expect(screen.queryByRole('textbox', { name: 'Name' })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Edit Name' })).toHaveFocus();
    expect(requestUrls(fetchMock, `PATCH /api/people/${ADA_ID}`)).toHaveLength(0);

    await userEvent.click(screen.getByRole('button', { name: 'Edit Name' }));
    await userEvent.clear(screen.getByRole('textbox', { name: 'Name' }));
    await userEvent.type(screen.getByRole('textbox', { name: 'Name' }), 'Ada King{Enter}');
    await waitFor(() => expect(requestUrls(fetchMock, `PATCH /api/people/${ADA_ID}`)).toHaveLength(1));
    expect(await screen.findByText('Name saved')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Undo' }));
    await waitFor(() => expect(requestUrls(fetchMock, `PATCH /api/people/${ADA_ID}`)).toHaveLength(2));
    const bodies = fetchMock.mock.calls
      .filter(([, init]) => (init as RequestInit | undefined)?.method === 'PATCH')
      .map(([, init]) => JSON.parse(String((init as RequestInit).body)) as unknown);
    expect(bodies).toEqual([{ name: 'Ada King' }, { name: 'Ada Lovelace' }]);
  });

  it('saves the notes by itself once typing stops or the field loses focus', async () => {
    const fetchMock = mockFetch({
      [`GET /api/people/${ADA_ID}`]: adaProfile,
      'GET /api/conversations/search': conversationsPage,
      [`PATCH /api/people/${ADA_ID}`]: (_url: URL, init: RequestInit) =>
        jsonResponse({ ...adaProfile, notes: (JSON.parse(String(init.body)) as { notes: string }).notes }),
    });
    renderWithProviders(<PersonPage />, { route: `/contacts/${ADA_ID}`, path: '/contacts/:personId' });

    const notes = await screen.findByRole('textbox', { name: 'Notes' });
    expect(screen.queryByRole('button', { name: 'Save' })).not.toBeInTheDocument();
    await userEvent.clear(notes);
    await userEvent.type(notes, 'VIP customer');
    await userEvent.tab();
    await waitFor(() => expect(requestUrls(fetchMock, `PATCH /api/people/${ADA_ID}`)).toHaveLength(1));
    expect(await screen.findByText('Saved')).toBeInTheDocument();
    const body = JSON.parse(
      String(fetchMock.mock.calls.find(([, init]) => (init as RequestInit | undefined)?.method === 'PATCH')![1]!.body),
    ) as Record<string, string>;
    expect(body).toEqual({ notes: 'VIP customer' });

    await userEvent.type(notes, '!');
    await waitFor(() => expect(requestUrls(fetchMock, `PATCH /api/people/${ADA_ID}`)).toHaveLength(2), { timeout: 2000 });
  });

  it('lets admins edit attribute values as Admin, and support users only read them and the name of a signed-in user', async () => {
    const fetchMock = mockFetch({
      [`GET /api/people/${ADA_ID}`]: adaProfile,
      'GET /api/conversations/search': conversationsPage,
      [`PATCH /api/people/${ADA_ID}/attributes`]: { applied: ['plan'], rejected: [] },
    });
    const { unmount } = renderWithProviders(<PersonPage />, { route: `/contacts/${ADA_ID}`, path: '/contacts/:personId' });
    await screen.findByRole('heading', { level: 1, name: 'Ada Lovelace' });
    await userEvent.click(screen.getByRole('button', { name: 'Edit Plan' }));
    await userEvent.clear(screen.getByRole('textbox', { name: 'Plan' }));
    await userEvent.type(screen.getByRole('textbox', { name: 'Plan' }), 'enterprise{Enter}');
    await waitFor(() => expect(requestUrls(fetchMock, `PATCH /api/people/${ADA_ID}/attributes`)).toHaveLength(1));
    const call = fetchMock.mock.calls.find(([input]) => String(input).endsWith('/attributes'));
    expect(JSON.parse(String((call![1] as RequestInit).body))).toEqual({ attributes: { plan: 'enterprise' } });
    unmount();

    mockFetch({ [`GET /api/people/${ADA_ID}`]: adaProfile, 'GET /api/conversations/search': conversationsPage, 'GET /api/auth/me': supportUser });
    renderWithProviders(<PersonPage />, { route: `/contacts/${ADA_ID}`, path: '/contacts/:personId' });
    await screen.findByRole('heading', { level: 1, name: 'Ada Lovelace' });
    expect(screen.queryByRole('button', { name: 'Edit Name' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Edit Plan' })).not.toBeInTheDocument();
    expect(screen.getByRole('textbox', { name: 'Notes' })).toBeInTheDocument();
  });

  it('lets an admin erase the person only after typing their name', async () => {
    const fetchMock = mockFetch({
      [`GET /api/people/${ADA_ID}`]: adaProfile,
      'GET /api/conversations/search': conversationsPage,
      [`DELETE /api/people/${ADA_ID}`]: { conversations: 3, visitors: 1 },
      'GET /api/people': { items: [], nextCursor: null },
    });
    renderWithProviders(<PersonPage />, {
      route: `/contacts/${ADA_ID}`,
      path: '/contacts/:personId',
      extraRoutes: <Route path="/contacts" element={<p>Contacts list</p>} />,
    });

    await userEvent.click(await screen.findByRole('button', { name: 'Erase' }));
    const dialog = await screen.findByRole('dialog', { name: 'Erase Ada Lovelace?' });
    const erase = within(dialog).getByRole('button', { name: 'Erase person' });
    expect(erase).toBeDisabled();
    await userEvent.type(within(dialog).getByRole('textbox', { name: 'Type Ada Lovelace to confirm' }), 'Ada Lovelace');
    await userEvent.click(erase);

    expect(await screen.findByText('Contacts list')).toBeInTheDocument();
    const call = fetchMock.mock.calls.find(([, init]) => (init as RequestInit | undefined)?.method === 'DELETE');
    expect(JSON.parse(String((call![1] as RequestInit).body))).toEqual({ confirm: true });
  });

  it('hides export and erasure from support users', async () => {
    mockFetch({ [`GET /api/people/${ADA_ID}`]: adaProfile, 'GET /api/conversations/search': conversationsPage, 'GET /api/auth/me': supportUser });
    renderWithProviders(<PersonPage />, { route: `/contacts/${ADA_ID}`, path: '/contacts/:personId' });

    expect(await screen.findByRole('heading', { level: 1, name: 'Ada Lovelace' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Erase' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Export' })).not.toBeInTheDocument();
  });

  it('says when the person does not exist', async () => {
    mockFetch({
      [`GET /api/people/${ADA_ID}`]: () => problemResponse({ title: 'Not found' }, 404),
      'GET /api/conversations/search': conversationsPage,
    });
    renderWithProviders(<PersonPage />, { route: `/contacts/${ADA_ID}`, path: '/contacts/:personId' });

    expect(await screen.findByText('This person does not exist')).toBeInTheDocument();
  });
});

describe('PersonFilter', () => {
  it('finds a person to filter by and shows the chosen one with a way to remove it', async () => {
    mockFetch({ 'GET /api/people': { items: [ada], nextCursor: null }, [`GET /api/people/${ADA_ID}`]: adaProfile });
    const onChange = vi.fn();
    const { rerender } = renderWithProviders(<PersonFilter value="" onChange={onChange} />);

    await userEvent.type(screen.getByRole('searchbox', { name: 'Contact' }), 'ada');
    await userEvent.click(await screen.findByRole('button', { name: /Ada Lovelace/ }));
    expect(onChange).toHaveBeenCalledWith(ADA_ID);

    rerender(<PersonFilter value={ADA_ID} onChange={onChange} />);
    await userEvent.click(await screen.findByRole('button', { name: 'Remove filter: Contact: Ada Lovelace' }));
    expect(onChange).toHaveBeenLastCalledWith('');
  });
});
