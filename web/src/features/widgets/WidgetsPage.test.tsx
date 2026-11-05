import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, onTestFinished, vi } from 'vitest';
import { agentDto, agentGuid, agentsRoutes, builtInAgent } from '../../test/agentsApi';
import { emailSettings, integrationsRoutes, slackSettings, workingEmail } from '../../test/integrationsApi';
import { chooseOption } from '../../test/select';
import { systemInfoFixture } from '../../test/apiFixtures';
import { dismissAllToasts, Toaster } from '../../components/ui/Toast';
import { fetchCalls, jsonResponse, mockFetch, problemResponse, renderWithProviders } from '../../test/utils';
import { fakePreview } from '../../test/widgetPreview';
import { widgetDto, widgetGuid, widgetOptions, widgetsRoutes, widgetSettings } from '../../test/widgetsApi';
import { normalizeSettings } from './api';
import type { SaveWidgetRequest, WidgetDto } from './types';
import { initialWidgetValues, isSiteOrigin, toPreviewConfig, toSaveRequest, validateWidget } from './widgetForm';
import { WidgetsPage } from './WidgetsPage';

afterEach(() => {
  vi.unstubAllGlobals();
});

const helper = agentDto();
const shop = widgetDto();

function routes(getWidgets: () => readonly WidgetDto[], extra: Record<string, unknown> = {}) {
  return {
    ...widgetsRoutes(getWidgets),
    ...agentsRoutes(() => [builtInAgent, helper]),
    ...integrationsRoutes(() => ({ email: emailSettings(), slack: slackSettings() })),
    ...extra,
  };
}

function renderAt(path: string) {
  return renderWithProviders(<WidgetsPage />, { route: path, path: '/widgets/:widgetId?' });
}

function lastBody(fetchMock: ReturnType<typeof mockFetch>, method: string): SaveWidgetRequest {
  const call = fetchMock.mock.calls.filter(([, init]) => (init as RequestInit | undefined)?.method === method).at(-1);
  return JSON.parse((call![1] as RequestInit).body as string) as SaveWidgetRequest;
}

function settings() {
  return screen.getByRole('region', { name: 'Settings' });
}

// The theme's Mode has radios with the same names.
function previewScheme(name: 'Light' | 'Dark') {
  return within(screen.getByRole('region', { name: 'Preview' })).getByRole('radio', { name });
}

// The tabs only show with the element list.
async function showTab(name: 'Elements' | 'Theme' | 'Settings') {
  const back = within(settings()).queryByRole('button', { name: 'All elements' });
  if (back) {
    await userEvent.click(back);
  }
  await userEvent.click(within(settings()).getByRole('tab', { name }));
}

async function outline(name: string) {
  if (within(settings()).queryByRole('tab', { name: 'Elements', selected: true }) === null) {
    await showTab('Elements');
  }
  return within(settings()).getByRole('button', { name: new RegExp(`^${name}`) });
}

async function elementView(name: string) {
  await within(settings()).findByRole('heading', { level: 2, name });
  return settings();
}

describe('WidgetsPage', () => {
  it('shows each widget as a card with its agent, status, sites and the conversations of the last 7 days', async () => {
    const docs = widgetDto({
      id: widgetGuid(2),
      name: 'Docs help',
      enabled: false,
      publicKey: 'wk_docs',
      agentId: agentGuid(9),
      settings: widgetSettings(),
    });
    mockFetch(
      routes(() => [shop, docs], {
        'GET /api/overview': {
          days: 7,
          from: '2026-10-28T09:00:00Z',
          conversations: 12,
          websiteConversations: 12,
          previousConversations: 0,
          answeredByAi: 12,
          handedToPerson: 0,
          averageRating: null,
          ratings: 0,
          widgets: [
            { widgetId: shop.id, conversations: 12, lastConversationAt: '2026-11-04T08:00:00Z' },
            { widgetId: docs.id, conversations: 0, lastConversationAt: null },
          ],
        },
      }),
    );
    renderAt('/widgets');
    const card = (name: string) => screen.getByRole('article', { name });

    expect(await screen.findByRole('heading', { level: 1, name: 'Widgets' })).toBeInTheDocument();
    const list = await screen.findByRole('list', { name: 'Widgets' });
    expect(
      within(list)
        .getAllByRole('heading', { level: 2 })
        .map((heading) => heading.textContent),
    ).toEqual(['Docs help', 'Shop help']);

    const shopCard = card('Shop help');
    expect(within(shopCard).getByRole('link', { name: 'Shop help' })).toHaveAttribute('href', `/widgets/${shop.id}`);
    expect(within(shopCard).getByText('Enabled')).toBeInTheDocument();
    expect(within(shopCard).getByText('On shop.example.com')).toBeInTheDocument();
    expect(await within(shopCard).findByText('Website helper')).toBeInTheDocument();
    expect(await within(shopCard).findByText('12 conversations in 7 days')).toBeInTheDocument();

    const docsCard = card('Docs help');
    expect(within(docsCard).getByText('Disabled')).toBeInTheDocument();
    expect(within(docsCard).getByText('Any site may embed it')).toBeInTheDocument();
    expect(within(docsCard).getByText('Unknown agent')).toBeInTheDocument();
    expect(within(docsCard).getByText('No conversations yet')).toBeInTheDocument();
  });

  it("shows a still of each widget in its card, in the widget's preset, mode and colors", async () => {
    const solid = widgetDto({
      settings: widgetSettings({
        appearance: {
          title: 'Shop help',
          preset: 'solid',
          theme: 'dark',
          brandColor: '#7c3aed',
          launcherLabel: 'Ask us',
          colors: { light: {}, dark: { header: '#112233' } },
        },
        behavior: { greeting: 'Hello from the shop' },
      }),
    });
    const auto = widgetDto({
      id: widgetGuid(2),
      name: 'Docs help',
      settings: widgetSettings({ appearance: { title: 'Docs bot', preset: 'glass', theme: 'auto' } }),
    });
    mockFetch(routes(() => [solid, auto]));
    document.documentElement.classList.add('dark');
    onTestFinished(() => document.documentElement.classList.remove('dark'));
    renderAt('/widgets');

    const card = await screen.findByRole('article', { name: 'Shop help' });
    const still = card.querySelector<HTMLElement>('[data-preset]')!;
    expect(still).toHaveAttribute('aria-hidden', 'true');
    expect(still).toHaveAttribute('data-preset', 'solid');
    expect(still).toHaveAttribute('data-mode', 'dark');
    expect(within(still).getByText('Shop help').closest('[data-thumbnail-header]')).toHaveStyle({ backgroundColor: 'rgb(17, 34, 51)' });
    expect(within(still).getByText('Hello from the shop')).toBeInTheDocument();
    expect(within(still).getByText('Ask us')).toBeInTheDocument();

    const docs = screen.getByRole('article', { name: 'Docs help' }).querySelector('[data-preset]')!;
    expect(docs).toHaveAttribute('data-preset', 'glass');
    expect(docs).toHaveAttribute('data-mode', 'dark');
    expect(within(docs as HTMLElement).getByText('Docs bot')).toBeInTheDocument();

    expect(still).toHaveStyle({ aspectRatio: '4 / 3' });
    const stage = still.querySelector<HTMLElement>('[data-thumbnail-stage]')!;
    expect(stage).toHaveStyle({ width: '896px', height: '672px' });
    expect(stage.style.transform).toMatch(/scale\(0?\.\d+\)/);
  });

  it('opens the real widget in a preview from the card menu', async () => {
    const user = userEvent.setup();
    mockFetch(routes(() => [shop]));
    renderAt('/widgets');
    const card = await screen.findByRole('article', { name: 'Shop help' });
    await user.click(within(card).getByRole('button', { name: 'Actions for Shop help' }));
    await user.click(await screen.findByRole('menuitem', { name: 'Preview' }));

    const dialog = await screen.findByRole('dialog', { name: 'Preview: Shop help' });
    expect(within(dialog).getByTitle('Shop help, live preview')).toHaveAttribute('src', '/widget/v1/preview');
    expect(dialog).toHaveTextContent('not counted in Analytics');
    expect(within(dialog).getByRole('button', { name: 'Reset conversation' })).toBeInTheDocument();
    await user.click(within(dialog).getByRole('button', { name: 'Close' }));
    expect(screen.queryByRole('dialog')).toBeNull();
  });

  it('loads what the editor needs when the pointer reaches a card, so the editor opens without a loading state', async () => {
    const fetchMock = mockFetch(routes(() => [shop]));
    renderAt('/widgets');
    const card = await screen.findByRole('article', { name: 'Shop help' });
    const optionsCalls = () => fetchCalls(fetchMock).filter((call) => call === 'GET /api/widgets/options').length;
    expect(optionsCalls()).toBe(0);

    await userEvent.hover(card);
    await waitFor(() => expect(optionsCalls()).toBe(1));
    expect(fetchCalls(fetchMock)).toContain('GET /api/integrations');

    await userEvent.click(within(card).getByRole('link', { name: 'Shop help' }));
    expect(screen.getByRole('region', { name: 'Preview' })).toBeInTheDocument();
    expect(screen.queryByText('Loading widget')).toBeNull();
    expect(optionsCalls()).toBe(1);
  });

  it("shows the editor's layout while it loads and keeps the preview hidden until the widget is drawn", async () => {
    let answerOptions: () => void = () => undefined;
    mockFetch(
      routes(() => [shop], {
        'GET /api/widgets/options': () => new Promise<Response>((resolve) => (answerOptions = () => resolve(jsonResponse(widgetOptions())))),
      }),
    );
    renderAt(`/widgets/${shop.id}`);

    expect(await screen.findByText('Loading widget')).toHaveAttribute('role', 'status');
    expect(await screen.findByText('Shop help')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'All widgets' })).toHaveAttribute('href', '/widgets');
    expect(screen.queryByRole('region', { name: 'Preview' })).toBeNull();

    answerOptions();
    const frame = await screen.findByTitle('Widget preview');
    expect(screen.queryByText('Loading widget')).toBeNull();
    expect(frame).toHaveClass('opacity-0');
    await fakePreview();
    await waitFor(() => expect(frame).toHaveClass('opacity-100'));
  });

  it('explains what a widget is while there is none', async () => {
    mockFetch(routes(() => []));
    renderAt('/widgets');

    expect(await screen.findByText('Put a chat on your website')).toBeInTheDocument();
    expect(screen.getAllByRole('link', { name: 'New widget' }).map((link) => link.getAttribute('href'))).toEqual(['/widgets/new', '/widgets/new']);
  });

  it('creates a widget in a short wizard: the agent is chosen first, then name and sites, the look and a person, and it opens in the editor', async () => {
    const user = userEvent.setup();
    let widgets: WidgetDto[] = [];
    const other = agentDto({ id: agentGuid(3), key: 'docs', name: 'Docs guide', description: null });
    const fetchMock = mockFetch({
      ...routes(() => widgets, {
        'POST /api/widgets': (_url: URL, init: RequestInit) => {
          const body = JSON.parse(init.body as string) as SaveWidgetRequest;
          const created = widgetDto({ ...body, id: widgetGuid(3), publicKey: 'wk_new' });
          widgets = [created];
          return jsonResponse(created, 201);
        },
      }),
      ...agentsRoutes(() => [builtInAgent, other, helper]),
    });
    onTestFinished(dismissAllToasts);
    renderWithProviders(
      <>
        <WidgetsPage />
        <Toaster />
      </>,
      { route: '/widgets/new', path: '/widgets/:widgetId?' },
    );

    expect(await screen.findByRole('heading', { level: 1, name: 'Which agent answers?' })).toBeInTheDocument();
    expect(screen.getByText('Step 1 of 4')).toBeInTheDocument();
    const agents = screen.getByRole('radiogroup', { name: 'Agent' });
    const choices = within(agents).getAllByRole('radio');
    expect(choices).toHaveLength(2);
    expect(choices[0]).toHaveAccessibleName('Docs guide');
    expect(choices[1]).toHaveAccessibleName('Website helper');
    expect(choices.every((radio) => !(radio as HTMLInputElement).checked)).toBe(true);
    expect(within(agents).queryByRole('radio', { name: /Knowledge Agent/ })).toBeNull();
    expect(within(agents).getByRole('radio', { name: 'Website helper' })).toHaveAccessibleDescription('Answers visitors of the public website.');
    expect(screen.getByRole('link', { name: 'Create an agent' })).toHaveAttribute('href', '/agents');

    await user.click(screen.getByRole('button', { name: 'Continue' }));
    expect(screen.getByRole('heading', { level: 1, name: 'Which agent answers?' })).toBeInTheDocument();
    expect(agents).toHaveAttribute('aria-invalid', 'true');
    expect(agents).toHaveAccessibleDescription('Choose an agent.');
    await waitFor(() => expect(choices[0]).toHaveFocus());
    await user.click(within(agents).getByRole('radio', { name: 'Website helper' }));
    expect(screen.queryByText('Choose an agent.')).toBeNull();
    await user.click(screen.getByRole('button', { name: 'Continue' }));

    expect(await screen.findByRole('heading', { level: 1, name: 'Name and sites' })).toHaveFocus();
    await user.click(screen.getByRole('button', { name: 'Continue' }));
    const name = screen.getByRole('textbox', { name: 'Name' });
    expect(name).toHaveAttribute('aria-invalid', 'true');
    await waitFor(() => expect(name).toHaveFocus());
    await user.type(name, 'Shop help');
    const origins = screen.getByRole('textbox', { name: 'Allowed sites' });
    expect(screen.getByText('Any site can embed this widget')).toBeInTheDocument();
    await user.type(origins, 'shop.example.com');
    await user.click(screen.getByRole('button', { name: 'Continue' }));
    expect(origins).toHaveAccessibleDescription(/'shop\.example\.com' is not a site such as/);
    await user.clear(origins);
    await user.type(origins, 'https://shop.example.com');
    await user.click(screen.getByRole('button', { name: 'Continue' }));

    expect(await screen.findByRole('heading', { level: 1, name: 'How it looks' })).toBeInTheDocument();
    await user.click(screen.getByRole('radio', { name: /^Solid/ }));
    await user.click(screen.getByRole('button', { name: 'Continue' }));

    expect(await screen.findByRole('heading', { level: 1, name: 'Talk to a person' })).toBeInTheDocument();
    expect(screen.queryByRole('combobox', { name: 'Send requests to' })).toBeNull();
    await user.click(screen.getByRole('switch', { name: 'Show Talk to a person' }));
    expect(screen.getByRole('link', { name: 'Website helper' })).toHaveAttribute('href', `/agents/${helper.id}`);
    await chooseOption(user, screen.getByRole('combobox', { name: 'Send requests to' }), 'Email');
    expect(screen.getByText('Email is not set up, so requests stay in the inbox.')).toBeInTheDocument();
    await user.type(screen.getByRole('textbox', { name: /Email addresses/ }), 'help@shop.example.com');
    await user.click(screen.getByRole('button', { name: 'Continue' }));

    expect(await screen.findByRole('heading', { level: 1, name: 'Review and create' })).toBeInTheDocument();
    expect(screen.getByText('Last check')).toBeInTheDocument();
    const review = screen.getByRole('region', { name: 'In short' });
    const rows = within(review)
      .getAllByRole('term')
      .map((term) => `${term.textContent}: ${term.nextElementSibling!.textContent}`);
    expect(rows).toEqual([
      'Agent: Website helper',
      'Name: Shop help',
      'Sites: https://shop.example.com',
      'Look: Solid, brand color #0f766e',
      'Talk to a person: On. Requests go to email and the Inbox.',
    ]);
    await user.click(within(review).getByRole('button', { name: 'Edit agent' }));
    expect(await screen.findByRole('heading', { level: 1, name: 'Which agent answers?' })).toBeInTheDocument();
    expect(screen.getByRole('radio', { name: 'Website helper' })).toBeChecked();
    for (const title of ['Name and sites', 'How it looks', 'Talk to a person', 'Review and create']) {
      await user.click(screen.getByRole('button', { name: 'Continue' }));
      await screen.findByRole('heading', { level: 1, name: title });
    }

    expect(fetchCalls(fetchMock)).not.toContain('POST /api/widgets');
    await user.click(await screen.findByRole('button', { name: 'Create widget' }));

    expect(await screen.findByRole('heading', { level: 1, name: 'Shop help' })).toBeInTheDocument();
    expect(await screen.findByText('Widget created')).toBeInTheDocument();
    expect(screen.getByText('Add the snippet to your site to show the chat.')).toHaveAttribute('role', 'status');
    expect(within(settings()).getByRole('tab', { name: 'Settings' })).toHaveAttribute('aria-selected', 'true');
    expect(within(settings()).getByRole('button', { name: 'Copy snippet' })).toBeInTheDocument();

    const body = lastBody(fetchMock, 'POST');
    const defaults = widgetSettings();
    expect(body).toMatchObject({ name: 'Shop help', agentId: helper.id, enabled: true });
    expect(body.settings.appearance).toEqual({ ...defaults.appearance, preset: 'solid' });
    expect(body.settings.security).toEqual({ ...defaults.security, allowedOrigins: ['https://shop.example.com'] });
    expect(body.settings.behavior).toEqual({ ...defaults.behavior, handoverEnabled: true });
    expect(body.settings.handover).toEqual({ ...defaults.handover, channel: 'email', emailTo: ['help@shop.example.com'] });
    expect(body.settings.conversations).toEqual(defaults.conversations);
  });

  it("opens the wizard with the agent chosen on the agent's page, and goes back to the step of a field the API refuses", async () => {
    const user = userEvent.setup();
    let attempts = 0;
    let widgets: WidgetDto[] = [];
    const fetchMock = mockFetch(
      routes(() => widgets, {
        'POST /api/widgets': (_url: URL, init: RequestInit) => {
          attempts += 1;
          if (attempts === 1) {
            return problemResponse(
              {
                title: 'One or more validation errors occurred.',
                errors: { 'settings.security.allowedOrigins': ["'https://shop.example.com' is used by another widget."] },
              },
              400,
            );
          }
          widgets = [widgetDto({ ...(JSON.parse(init.body as string) as SaveWidgetRequest), id: widgetGuid(4) })];
          return jsonResponse(widgets[0], 201);
        },
      }),
    );
    renderWithProviders(<WidgetsPage />, {
      route: { pathname: '/widgets/new', state: { agentId: helper.id } } as unknown as string,
      path: '/widgets/:widgetId?',
    });

    expect(await screen.findByRole('radio', { name: 'Website helper' })).toBeChecked();
    await user.click(screen.getByRole('button', { name: 'Continue' }));
    await user.type(await screen.findByRole('textbox', { name: 'Name' }), 'Shop help');
    await user.type(screen.getByRole('textbox', { name: 'Allowed sites' }), 'https://shop.example.com');
    for (const title of ['How it looks', 'Talk to a person', 'Review and create']) {
      await user.click(screen.getByRole('button', { name: 'Continue' }));
      await screen.findByRole('heading', { level: 1, name: title });
    }
    await user.click(screen.getByRole('button', { name: 'Create widget' }));

    expect(await screen.findByRole('heading', { level: 1, name: 'Name and sites' })).toBeInTheDocument();
    const origins = screen.getByRole('textbox', { name: 'Allowed sites' });
    expect(origins).toHaveAttribute('aria-invalid', 'true');
    expect(origins).toHaveAccessibleDescription("'https://shop.example.com' is used by another widget.");
    await waitFor(() => expect(origins).toHaveFocus());
    await user.clear(origins);
    await user.type(origins, 'https://help.example.com');
    expect(origins).not.toHaveAttribute('aria-invalid');

    for (const title of ['How it looks', 'Talk to a person', 'Review and create']) {
      await user.click(screen.getByRole('button', { name: 'Continue' }));
      await screen.findByRole('heading', { level: 1, name: title });
    }
    await user.click(screen.getByRole('button', { name: 'Create widget' }));
    expect(await screen.findByRole('heading', { level: 1, name: 'Shop help' })).toBeInTheDocument();
    expect(lastBody(fetchMock, 'POST')).toMatchObject({
      agentId: helper.id,
      settings: { security: { allowedOrigins: ['https://help.example.com'] } },
    });
  });

  it('points to the Agents page when there is no agent a widget may use', async () => {
    mockFetch({ ...routes(() => []), ...agentsRoutes(() => [builtInAgent]) });
    renderAt('/widgets/new');

    expect(await screen.findByText('No agent to answer yet')).toBeInTheDocument();
    expect(screen.getByText(/A widget needs an agent of its own/)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Create an agent' })).toHaveAttribute('href', '/agents');
    expect(screen.queryByRole('radio')).toBeNull();
    expect(screen.queryByRole('button', { name: 'Continue' })).toBeNull();
    expect(screen.getByRole('button', { name: 'Cancel' })).toBeInTheDocument();
  });

  it('edits what the admin clicks in the preview, shows every change there at once and saves the whole widget', async () => {
    const user = userEvent.setup();
    const fetchMock = mockFetch(
      routes(() => [shop], {
        [`PUT /api/widgets/${shop.id}`]: (_url: URL, init: RequestInit) => jsonResponse({ ...shop, ...(JSON.parse(init.body as string) as object) }),
      }),
    );
    renderAt(`/widgets/${shop.id}`);

    const preview = await fakePreview();
    await waitFor(() =>
      expect(preview.lastRender()).toEqual({
        type: 'render',
        config: expect.objectContaining({ key: shop.publicKey, title: 'Shop help', brandColor: '#0f766e', position: 'right' }),
        scene: 'start',
        colorScheme: 'light',
      }),
    );
    expect(screen.getByRole('button', { name: 'Save' })).toBeDisabled();

    preview.click('appearance.title');
    const view = await elementView('Title');
    expect(screen.queryByRole('dialog')).toBeNull();
    expect(within(view).getByText('Scene: Start')).toBeInTheDocument();
    await waitFor(() => expect(within(view).getByRole('heading', { level: 2, name: 'Title' })).toHaveFocus());
    const title = within(view).getByRole('textbox', { name: 'Title' });
    await user.clear(title);
    await user.type(title, 'Need help?');
    expect(preview.lastRender()).toMatchObject({ config: { title: 'Need help?' } });
    expect(screen.getByText('Unsaved changes')).toBeInTheDocument();

    preview.click(null);
    await waitFor(() => expect(within(settings()).queryByRole('heading', { level: 2, name: 'Title' })).toBeNull());
    expect(within(settings()).getByRole('tab', { name: 'Elements' })).toHaveAttribute('aria-selected', 'true');

    await user.click(await outline('Launcher'));
    const launcher = await elementView('Launcher');
    expect(within(launcher).getByText('The chat is open, so the launcher shows its close icon.')).toBeInTheDocument();
    await user.click(within(launcher).getByRole('button', { name: 'Show closed' }));
    await waitFor(() => expect(preview.lastRender()?.scene).toBe('launcher'));
    await user.click(within(settings()).getByRole('radio', { name: 'Bottom left' }));
    expect(preview.lastRender()).toMatchObject({ config: { position: 'left' }, scene: 'launcher' });

    await user.click(screen.getByRole('radio', { name: 'Dark' }));
    expect(preview.lastRender()).toMatchObject({ colorScheme: 'dark' });
    await user.click(screen.getByRole('radio', { name: 'Phone' }));
    expect(screen.getByRole('radio', { name: 'Phone' })).toBeChecked();

    await user.click(screen.getByRole('button', { name: 'Save' }));
    expect(await screen.findByText('Widget saved.')).toHaveAttribute('role', 'status');
    const body = lastBody(fetchMock, 'PUT');
    expect(body.settings.appearance).toMatchObject({ title: 'Need help?', position: 'left' });
    expect(body.settings.security.allowedOrigins).toEqual(['https://shop.example.com']);
    expect(screen.getByRole('button', { name: 'Save' })).toBeDisabled();
  });

  it('opens an element of another scene from the outline, and points at what holds it while it is turned off', async () => {
    const user = userEvent.setup();
    mockFetch(routes(() => [shop]));
    renderAt(`/widgets/${shop.id}`);
    const preview = await fakePreview();
    await waitFor(() => expect(preview.lastRender()?.scene).toBe('start'));

    await user.click(await outline('Talk to a person'));
    await waitFor(() => expect(preview.lastRender()?.scene).toBe('conversation'));
    expect(screen.getByRole('combobox', { name: 'Scene' })).toHaveTextContent('Conversation');
    expect(preview.sent.at(-1)).toEqual({ type: 'track', target: 'behavior.handover', fallbacks: ['behavior.placeholder'] });
    const handover = await elementView('Talk to a person');
    expect(within(handover).getByText('Talk to a person does not show in the preview now.')).toBeInTheDocument();
    expect(within(handover).getByText('Scene: Conversation, not shown now')).toBeInTheDocument();

    await user.click(within(handover).getByRole('switch', { name: 'Show Talk to a person' }));
    expect(preview.lastRender()).toMatchObject({ config: { handoverEnabled: true } });
    await waitFor(() => expect(within(handover).queryByText('Talk to a person does not show in the preview now.')).toBeNull());
    expect(within(handover).getByText('Scene: Conversation')).toBeInTheDocument();

    await user.click(within(handover).getByRole('button', { name: 'All elements' }));
    await waitFor(() => expect(within(settings()).getByRole('button', { name: /^Talk to a person/ })).toHaveFocus());
  });

  it('saves an uploaded avatar and a replaced text from the side panel', async () => {
    const user = userEvent.setup();
    const assetId = '0199c1e2-0000-7000-8000-0000000c0001';
    const fetchMock = mockFetch(
      routes(() => [shop], {
        [`POST /api/widgets/${shop.id}/assets`]: () =>
          jsonResponse({ id: assetId, url: `/widget/v1/media/${assetId}`, contentType: 'image/png', size: 120 }, 201),
        [`PUT /api/widgets/${shop.id}`]: (_url: URL, init: RequestInit) => jsonResponse({ ...shop, ...(JSON.parse(init.body as string) as object) }),
      }),
    );
    renderAt(`/widgets/${shop.id}`);
    const preview = await fakePreview();

    await user.click(await outline('Avatar'));
    await elementView('Avatar');
    fireEvent.change(screen.getByLabelText('Avatar file'), { target: { files: [new File(['GIF89a'], 'avatar.gif', { type: 'image/gif' })] } });
    expect(screen.getByText('Choose a PNG, JPEG or WebP image.')).toBeInTheDocument();
    expect(fetchCalls(fetchMock)).not.toContain(`POST /api/widgets/${shop.id}/assets`);
    const file = new File([new Uint8Array([0x89, 0x50, 0x4e, 0x47])], 'avatar.png', { type: 'image/png' });
    fireEvent.change(screen.getByLabelText('Avatar file'), { target: { files: [file] } });
    expect(await screen.findByRole('img', { name: 'Current avatar' })).toHaveAttribute('src', `/widget/v1/media/${assetId}`);
    expect(preview.lastRender()).toMatchObject({ config: { avatarUrl: `/widget/v1/media/${assetId}` } });

    await showTab('Settings');
    await user.click(within(settings()).getByRole('button', { name: 'Other texts' }));
    const offline = screen.getByRole('textbox', { name: 'Offline notice' });
    expect(offline).toHaveAttribute('placeholder', 'Connection lost. Trying to reconnect.');
    await user.type(offline, 'Verbindung verloren.');
    await user.click(screen.getByRole('button', { name: 'Save' }));

    expect(await screen.findByText('Widget saved.')).toHaveAttribute('role', 'status');
    const body = lastBody(fetchMock, 'PUT');
    expect(body.name).toBe('Shop help');
    expect(body.settings.appearance.avatarAssetId).toBe(assetId);
    expect(body.settings.behavior.texts).toEqual({ offline: 'Verbindung verloren.' });
  });

  it('undoes, redoes and discards changes', async () => {
    const user = userEvent.setup();
    mockFetch(routes(() => [shop]));
    renderAt(`/widgets/${shop.id}`);
    await fakePreview();

    await showTab('Settings');
    await user.click(within(settings()).getByRole('button', { name: 'General' }));
    const name = screen.getByRole('textbox', { name: 'Name' });
    await user.type(name, ' 2');
    await user.click(screen.getByRole('switch', { name: 'Enabled' }));
    expect(screen.getByRole('switch', { name: 'Enabled' })).not.toBeChecked();

    await user.keyboard('{Control>}z{/Control}');
    expect(screen.getByRole('switch', { name: 'Enabled' })).toBeChecked();
    await user.click(screen.getByRole('button', { name: 'Undo' }));
    expect(name).toHaveValue('Shop help');
    expect(screen.getByRole('button', { name: 'Undo' })).toBeDisabled();
    await user.click(screen.getByRole('button', { name: 'Redo' }));
    expect(name).toHaveValue('Shop help 2');
    expect(screen.getByText('Unsaved changes')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Discard' }));
    expect(name).toHaveValue('Shop help');
    expect(screen.queryByText('Unsaved changes')).toBeNull();
    await user.click(screen.getByRole('button', { name: 'Undo' }));
    expect(name).toHaveValue('Shop help 2');
  });

  it('warns about any site embedding the widget, low contrast and a handover channel that is not set up', async () => {
    const user = userEvent.setup();
    mockFetch(routes(() => [widgetDto({ settings: widgetSettings() })]));
    renderAt(`/widgets/${shop.id}`);
    await fakePreview();

    await showTab('Settings');
    await user.click(within(settings()).getByRole('button', { name: 'Security and limits' }));
    expect(screen.getByText('Any site can embed this widget')).toBeInTheDocument();

    await user.click(await outline('Header'));
    const header = await elementView('Header');
    expect(within(header).queryByRole('textbox', { name: 'Brand color' })).toBeNull();
    await user.click(within(header).getByRole('button', { name: 'Theme' }));
    const color = within(settings()).getByRole('textbox', { name: 'Brand color' });
    // The widget picks dark text on yellow.
    await user.clear(color);
    await user.type(color, '#facc15');
    expect(screen.queryByText('Text on this color is hard to read')).toBeNull();
    await user.clear(color);
    await user.type(color, '#8b5cf6');
    expect(within(settings()).getByText('Text on this color is hard to read')).toBeInTheDocument();
    expect(within(settings()).getByText(/Dark text on #8b5cf6 reaches only 4\.46:1/)).toBeInTheDocument();

    await showTab('Settings');
    await user.click(within(settings()).getByRole('button', { name: 'Handover' }));
    await chooseOption(user, screen.getByRole('combobox', { name: 'Send requests to' }), 'Email');
    expect(screen.getByText('Email is not set up, so requests stay in the inbox.')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Open Notifications' })).toHaveAttribute('href', '/notifications');
  });

  it('names the agent that answers, and under General what belongs to the agent rather than the widget', async () => {
    const user = userEvent.setup();
    mockFetch({ ...routes(() => [shop]), ...integrationsRoutes(() => ({ email: workingEmail, slack: slackSettings() })) });
    renderAt(`/widgets/${shop.id}`);

    const answeredBy = await within(await screen.findByRole('region', { name: 'Settings' })).findByTestId('answered-by');
    expect(answeredBy).toHaveTextContent('Answered by: Website helper');
    expect(within(answeredBy).getByRole('link', { name: 'Website helper' })).toHaveAttribute('href', `/agents/${shop.agentId}`);
    expect(within(answeredBy).queryByTestId('widget-changes')).toBeNull();

    await showTab('Settings');
    await user.click(within(settings()).getByRole('button', { name: 'General' }));
    const summary = within(settings()).getByTestId('widget-changes');
    expect(summary).toHaveTextContent(
      'The agent decides what it knows and does. This widget decides where people meet it, and can switch agent features off.',
    );
    expect(summary).toHaveTextContent('From the agent');
    expect(summary).toHaveTextContent('Answers from');
    expect(summary).toHaveTextContent('This widget');
    expect(summary).toHaveTextContent('Runs on');
    expect(summary).toHaveTextContent(shop.settings.behavior.handoverEnabled ? /A personThrough/ : /A personOff on this widget/);
    expect(summary).not.toHaveTextContent('Message length');
  });

  it('tests the real widget in a preview, with the saved settings', async () => {
    const user = userEvent.setup();
    mockFetch({ ...routes(() => [shop]), ...integrationsRoutes(() => ({ email: workingEmail, slack: slackSettings() })) });
    renderAt(`/widgets/${shop.id}`);

    await user.click(await screen.findByRole('button', { name: 'Test this widget' }));
    const dialog = await screen.findByRole('dialog', { name: 'Preview: Shop help' });
    expect(dialog).toHaveTextContent('The real widget with its saved settings');
    expect(within(dialog).getByRole('radiogroup', { name: 'Device' })).toBeInTheDocument();
    expect(within(dialog).getByRole('radiogroup', { name: "Visitor's color scheme" })).toBeInTheDocument();
    await user.keyboard('{Escape}');
    expect(screen.queryByRole('dialog', { name: 'Preview: Shop help' })).toBeNull();
  });

  it('opens the preview at once when the Playground links to it', async () => {
    mockFetch({ ...routes(() => [shop]), ...integrationsRoutes(() => ({ email: workingEmail, slack: slackSettings() })) });
    renderAt(`/widgets/${shop.id}?preview`);
    expect(await screen.findByRole('dialog', { name: 'Preview: Shop help' })).toBeInTheDocument();
  });

  it('says nothing about a handover channel that works', async () => {
    const user = userEvent.setup();
    mockFetch({ ...routes(() => [shop]), ...integrationsRoutes(() => ({ email: workingEmail, slack: slackSettings() })) });
    renderAt(`/widgets/${shop.id}`);

    await user.click(await within(await screen.findByRole('region', { name: 'Settings' })).findByRole('tab', { name: 'Settings' }));
    await user.click(within(settings()).getByRole('button', { name: 'Handover' }));
    await chooseOption(user, screen.getByRole('combobox', { name: 'Send requests to' }), 'Email');
    expect(screen.queryByText(/Email is not set up/)).toBeNull();
    expect(screen.queryByRole('link', { name: 'Open Notifications' })).toBeNull();
    expect(within(settings()).getByText(/When the assistant offers a person is set on the agent/)).toBeInTheDocument();
    const agentLinks = within(settings()).getAllByRole('link', { name: 'Website helper' });
    expect(agentLinks).toHaveLength(2);
    agentLinks.forEach((link) => expect(link).toHaveAttribute('href', `/agents/${shop.agentId}`));
    expect(within(settings()).queryByRole('switch', { name: /offers a person when it cannot answer/ })).toBeNull();
    await chooseOption(user, screen.getByRole('combobox', { name: 'Send requests to' }), 'Slack');
    expect(screen.getByText(/Slack is not set up/)).toBeInTheDocument();
  });

  it('sets when quiet conversations close and how long a request waits for a person', async () => {
    const user = userEvent.setup();
    const fetchMock = mockFetch(
      routes(() => [shop], {
        [`PUT /api/widgets/${shop.id}`]: (_url: URL, init: RequestInit) => jsonResponse({ ...shop, ...(JSON.parse(init.body as string) as object) }),
      }),
    );
    renderAt(`/widgets/${shop.id}`);
    await fakePreview();

    await showTab('Settings');
    await user.click(within(settings()).getByRole('button', { name: 'Conversations' }));
    expect(screen.getByRole('switch', { name: 'Close quiet conversations' })).toBeChecked();
    const aiChats = screen.getByRole('spinbutton', { name: 'AI chats after (minutes)' });
    expect(aiChats).toHaveValue(30);
    expect(screen.getByRole('spinbutton', { name: 'Chats with a person after (hours)' })).toHaveValue(24);
    await user.clear(aiChats);
    await user.type(aiChats, '45');
    await user.click(within(settings()).getByRole('button', { name: 'Handover' }));
    const wait = screen.getByRole('spinbutton', { name: 'Stop waiting for a person after (minutes)' });
    expect(wait).toHaveValue(15);
    await user.clear(wait);
    await user.type(wait, '5');
    await user.click(screen.getByRole('button', { name: 'Save' }));

    expect(await screen.findByText('Widget saved.')).toBeInTheDocument();
    expect(lastBody(fetchMock, 'PUT').settings.conversations).toEqual({ closeIdle: true, aiIdleMinutes: 45, humanIdleHours: 24 });
    expect(lastBody(fetchMock, 'PUT').settings.handover.waitMinutes).toBe(5);

    await user.click(screen.getByRole('switch', { name: 'Close quiet conversations' }));
    expect(screen.queryByRole('spinbutton', { name: 'AI chats after (minutes)' })).toBeNull();
    await user.click(screen.getByRole('button', { name: 'Save' }));
    await waitFor(() =>
      expect(lastBody(fetchMock, 'PUT').settings.conversations).toEqual({ closeIdle: false, aiIdleMinutes: 45, humanIdleHours: 24 }),
    );
  });

  it('copies the embed snippet', async () => {
    const user = userEvent.setup();
    mockFetch(routes(() => [shop]));
    renderAt(`/widgets/${shop.id}`);
    await screen.findByTitle('Widget preview');
    await showTab('Settings');

    const snippet = `<script async src="https://yourdomain.com/widget/v1/loader.js" data-widget="${shop.publicKey}"></script>`;
    expect(await screen.findByText(snippet)).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Copy snippet' }));
    expect(await navigator.clipboard.readText()).toBe(snippet);
    expect(await screen.findByRole('button', { name: 'Copied' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Set your public address' })).toHaveAttribute('href', '/system#public-address');
  });

  it('builds the embed snippet from the public address set under Settings > System', async () => {
    mockFetch({
      ...routes(() => [shop]),
      'GET /api/system/info': systemInfoFixture({ publicAddress: { url: 'https://support.acme.com', source: 'saved' } }),
    });
    renderAt(`/widgets/${shop.id}`);
    await screen.findByTitle('Widget preview');
    await showTab('Settings');

    expect(
      await screen.findByText(`<script async src="https://support.acme.com/widget/v1/loader.js" data-widget="${shop.publicKey}"></script>`),
    ).toBeInTheDocument();
    expect(screen.queryByText(/yourdomain\.com/)).toBeNull();
    expect(screen.queryByRole('link', { name: 'Set your public address' })).toBeNull();
  });

  it('turns on identity verification, shows the secret once and the embed code for signed-in users', async () => {
    const user = userEvent.setup();
    const fetchMock = mockFetch({
      ...routes(() => [shop]),
      [`POST /api/widgets/${shop.id}/identity-secret`]: { secret: 'ka_ivs_0123456789abcdef', createdAt: '2026-11-05T10:00:00Z' },
    });
    renderAt(`/widgets/${shop.id}`);
    await screen.findByTitle('Widget preview');
    await showTab('Settings');

    await user.click(await screen.findByRole('button', { name: 'Turn on identity verification' }));

    expect(await screen.findByText('ka_ivs_0123456789abcdef')).toBeInTheDocument();
    expect(screen.getByText('Copy the secret now')).toBeInTheDocument();
    expect(screen.getByText(/data-user-id="USER_ID" data-user-hash="USER_HASH"/)).toBeInTheDocument();
    expect(screen.getByText(/window\.KnowledgeAgent\.logout\(\)/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Create new secret' })).toBeInTheDocument();

    expect(screen.getByText(/window\.KnowledgeAgent\.update\(\{ attributes:/)).toBeInTheDocument();
    expect(screen.getByText(/X-KA-Signature/)).toHaveTextContent(`/api/public/widgets/${shop.publicKey}/contacts/`);
    await user.click(screen.getByRole('radio', { name: 'Python' }));
    expect(screen.getByText(/hmac\.new\(secret\.encode\(\), f'\{timestamp\}\.\{user_id\}\.\{body\}'/)).toBeInTheDocument();
    await user.click(screen.getByRole('radio', { name: 'C#' }));
    expect(screen.getByText(/HMACSHA256\.HashData/)).toBeInTheDocument();
    expect(
      fetchMock.mock.calls.some(
        ([input, init]) => String(input).endsWith(`/api/widgets/${shop.id}/identity-secret`) && (init as RequestInit).method === 'POST',
      ),
    ).toBe(true);
  });

  it('picks a theme, overrides colors for the mode the preview shows and puts them back to the theme', async () => {
    const user = userEvent.setup();
    const fetchMock = mockFetch(
      routes(() => [shop], {
        [`PUT /api/widgets/${shop.id}`]: (_url: URL, init: RequestInit) => jsonResponse({ ...shop, ...(JSON.parse(init.body as string) as object) }),
      }),
    );
    renderAt(`/widgets/${shop.id}`);
    const preview = await fakePreview();
    await showTab('Theme');
    const panel = settings();

    expect(within(panel).getByRole('radio', { name: /^Glow/ })).toBeChecked();
    await user.click(within(panel).getByRole('radio', { name: /^Solid/ }));
    expect(preview.lastRender()).toMatchObject({ config: { preset: 'solid' } });

    expect(within(panel).getByRole('heading', { name: 'Colors in light mode' })).toBeInTheDocument();
    await user.click(within(panel).getByRole('button', { name: 'Header: theme' }));
    const picker = await screen.findByRole('dialog', { name: 'Header color' });
    const hex = within(picker).getByRole('textbox', { name: 'Hex color' });
    await waitFor(() => expect(hex).toHaveFocus());
    expect(hex).toHaveAttribute('placeholder', '#0f766e');
    expect(within(picker).getByText('#0f766e')).toBeInTheDocument();
    await user.type(hex, '#1E3A8A');
    expect(preview.lastRender()).toMatchObject({ config: { colors: { light: { header: '#1e3a8a' }, dark: {} } } });
    expect(within(panel).getByRole('button', { name: 'Header: #1E3A8A' })).toBeInTheDocument();
    await user.click(within(picker).getByRole('button', { name: 'Reset to theme' }));
    expect(hex).toHaveValue('');
    await user.click(within(picker).getByRole('button', { name: 'Use #ffffff' }));
    expect(within(panel).getByRole('button', { name: 'Header: #ffffff' })).toBeInTheDocument();
    await user.keyboard('{Escape}');
    expect(screen.queryByRole('dialog', { name: 'Header color' })).toBeNull();
    expect(within(panel).getByRole('button', { name: 'Header: #ffffff' })).toHaveFocus();
    await user.click(within(panel).getByRole('button', { name: 'Reset header to the theme' }));
    expect(within(panel).getByRole('button', { name: 'Header: theme' })).toBeInTheDocument();

    await user.click(within(panel).getByRole('button', { name: 'Text: theme' }));
    await user.type(within(await screen.findByRole('dialog', { name: 'Text color' })).getByRole('textbox', { name: 'Hex color' }), '#cccccc');
    expect(within(panel).getByRole('button', { name: 'Text: #cccccc' })).toHaveAccessibleDescription(
      /^Reaches only 1\.\d\d:1 on panel tint, below 4\.5:1\.$/,
    );
    await user.keyboard('{Escape}');
    await user.click(within(panel).getByRole('button', { name: 'Launcher: theme' }));
    const launcherHex = within(await screen.findByRole('dialog', { name: 'Launcher color' })).getByRole('textbox', { name: 'Hex color' });
    await user.type(launcherHex, '#zz');
    expect(launcherHex).toHaveAccessibleDescription('Enter a color such as #0f766e.');
    await user.clear(launcherHex);
    await user.keyboard('{Escape}');

    await user.click(previewScheme('Dark'));
    expect(within(panel).getByRole('heading', { name: 'Colors in dark mode' })).toBeInTheDocument();
    expect(within(panel).getByRole('button', { name: 'Text: theme' })).toBeInTheDocument();
    await user.click(within(panel).getByRole('button', { name: 'Glow: theme' }));
    await user.type(within(await screen.findByRole('dialog', { name: 'Glow color' })).getByRole('textbox', { name: 'Hex color' }), '#f472b6');
    await user.keyboard('{Escape}');
    await user.click(within(panel).getByRole('button', { name: 'Launcher: theme' }));
    await user.type(within(await screen.findByRole('dialog', { name: 'Launcher color' })).getByRole('textbox', { name: 'Hex color' }), '#111111');
    await user.keyboard('{Escape}');
    await user.click(within(panel).getByRole('button', { name: 'All to theme' }));
    expect(within(panel).getByRole('button', { name: 'Glow: theme' })).toBeInTheDocument();
    expect(within(panel).getByRole('button', { name: 'All to theme' })).toBeDisabled();
    await user.click(screen.getByRole('button', { name: 'Undo' }));
    expect(within(panel).getByRole('button', { name: 'Glow: #f472b6' })).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Save' }));
    expect(await screen.findByText('Widget saved.')).toBeInTheDocument();
    expect(lastBody(fetchMock, 'PUT').settings.appearance).toMatchObject({
      preset: 'solid',
      colors: { light: { text: '#cccccc' }, dark: { glow: '#f472b6', launcher: '#111111' } },
    });
  });

  it('drills into a clicked element: its texts and behavior first, then compact color rows, and fixes a color error in the mode it belongs to', async () => {
    const user = userEvent.setup();
    let attempts = 0;
    mockFetch(
      routes(() => [shop], {
        [`PUT /api/widgets/${shop.id}`]: () => {
          attempts += 1;
          return problemResponse(
            { title: 'One or more validation errors occurred.', errors: { 'settings.appearance.colors.dark': ["'shadow' is not a color."] } },
            400,
          );
        },
      }),
    );
    renderAt(`/widgets/${shop.id}`);
    const preview = await fakePreview();

    preview.click('behavior.placeholder');
    const box = await elementView('Message box');
    const fields = within(box)
      .getAllByRole('textbox')
      .map((field) => field.getAttribute('id') ?? '');
    expect(fields[0]).toMatch(/placeholder$/);
    const colors = within(box).getByRole('region', { name: 'Colors' });
    expect(
      within(colors)
        .getAllByRole('button', { name: /: theme$/ })
        .map((row) => row.getAttribute('aria-label')),
    ).toEqual(['Accent: theme', 'Text on accent: theme']);
    expect(within(colors).getByText(/The light colors, as the preview shows/)).toBeInTheDocument();

    await user.click(within(box).getByRole('button', { name: 'All elements' }));
    await chooseOption(user, screen.getByRole('combobox', { name: 'Scene' }), 'Conversation');
    await waitFor(() => expect(preview.lastRender()?.scene).toBe('conversation'));
    preview.click('messages.visitor');
    const bubble = await elementView("Visitor's message");
    await user.click(within(bubble).getByRole('button', { name: "Visitor's bubble: theme" }));
    await user.type(
      within(await screen.findByRole('dialog', { name: "Visitor's bubble color" })).getByRole('textbox', { name: 'Hex color' }),
      '#facc15',
    );
    expect(preview.lastRender()).toMatchObject({ config: { colors: { light: { userBubble: '#facc15' } } } });
    expect(within(bubble).getByRole('button', { name: "Visitor's bubble text: theme" })).not.toHaveAccessibleDescription();
    await user.keyboard('{Escape}');

    await user.click(screen.getByRole('button', { name: 'Save' }));
    await user.click(await screen.findByRole('button', { name: '1 to fix' }));
    const fix = await screen.findByRole('menu', { name: 'Settings to fix' });
    await user.click(within(fix).getByRole('menuitem', { name: "Colors in dark mode: 'shadow' is not a color." }));
    expect(previewScheme('Dark')).toBeChecked();
    expect(within(settings()).getByRole('tab', { name: 'Theme' })).toHaveAttribute('aria-selected', 'true');
    expect(within(settings()).getByRole('heading', { name: 'Colors in dark mode' })).toBeInTheDocument();
    expect(attempts).toBe(1);
  });

  it('picks the launcher icon from a small picker', async () => {
    const user = userEvent.setup();
    mockFetch(routes(() => [shop]));
    renderAt(`/widgets/${shop.id}`);
    const preview = await fakePreview();

    preview.click('appearance.launcher');
    const launcher = await elementView('Launcher');
    const icon = within(launcher).getByRole('button', { name: 'Icon Speech bubble' });
    await user.click(icon);
    const picker = await screen.findByRole('dialog', { name: 'Icon' });
    await waitFor(() => expect(within(picker).getByRole('button', { name: 'Speech bubble', pressed: true })).toHaveFocus());
    await user.click(within(picker).getByRole('button', { name: 'Headset' }));
    expect(screen.queryByRole('dialog', { name: 'Icon' })).toBeNull();
    expect(within(launcher).getByRole('button', { name: 'Icon Headset' })).toHaveFocus();
    expect(preview.lastRender()).toMatchObject({ config: { launcherIcon: 'headset' } });
    await user.click(within(launcher).getByRole('button', { name: 'Icon Headset' }));
    await screen.findByRole('dialog', { name: 'Icon' });
    await user.click(within(launcher).getByRole('heading', { level: 2, name: 'Launcher' }));
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Icon' })).toBeNull());
  });

  it('marks what changed since the last save, puts one setting back, and finds settings by name', async () => {
    const user = userEvent.setup();
    mockFetch(routes(() => [shop]));
    renderAt(`/widgets/${shop.id}`);
    const preview = await fakePreview();

    preview.click('appearance.title');
    const view = await elementView('Title');
    await user.type(within(view).getByRole('textbox', { name: 'Title' }), '!');
    expect(within(view).getByRole('button', { name: 'Edited. Reset title to the saved value' })).toBeInTheDocument();
    await user.keyboard('{Escape}');
    expect(await outline('Title')).toHaveAccessibleName('Title, edited');
    expect(await outline('Title')).toHaveFocus();
    expect(await outline('Header')).toHaveAccessibleName('Header');

    await showTab('Settings');
    await user.click(within(settings()).getByRole('button', { name: 'General' }));
    await user.click(screen.getByRole('switch', { name: 'Enabled' }));
    expect(within(settings()).getByRole('button', { name: /^General/ })).toHaveTextContent('1 edited');
    await user.click(screen.getByRole('button', { name: 'Edited. Reset enabled to the saved value' }));
    expect(screen.getByRole('switch', { name: 'Enabled' })).toBeChecked();
    expect(within(settings()).getByRole('button', { name: /^General/ })).not.toHaveTextContent('edited');

    const search = within(settings()).getByRole('searchbox', { name: 'Find a setting' });
    await user.type(search, 'glow');
    const results = () => within(settings()).getByRole('region', { name: 'Search results' });
    expect(within(results()).getByRole('status')).toHaveTextContent('1 setting');
    expect(within(results()).getByRole('button', { name: 'Glow: theme' })).toBeInTheDocument();
    expect(within(settings()).queryByRole('tab', { name: 'Settings' })).toBeNull();
    await user.clear(search);
    await user.type(search, 'rate');
    expect(within(results()).getByRole('switch', { name: 'Visitors can rate answers' })).toBeInTheDocument();
    await user.type(search, '{Escape}');
    expect(search).toHaveValue('');
    expect(within(settings()).getByRole('tab', { name: 'Settings' })).toBeInTheDocument();
  });

  it('hides the footer from its view, outlines the message box while it is hidden, and saves with the keyboard', async () => {
    const user = userEvent.setup();
    const fetchMock = mockFetch(
      routes(() => [shop], {
        [`PUT /api/widgets/${shop.id}`]: (_url: URL, init: RequestInit) => jsonResponse({ ...shop, ...(JSON.parse(init.body as string) as object) }),
      }),
    );
    renderAt(`/widgets/${shop.id}`);
    const preview = await fakePreview();

    preview.click('texts.poweredBy');
    const footer = await elementView('Footer');
    const toggle = within(footer).getByRole('switch', { name: 'Show the footer' });
    expect(toggle).toBeChecked();
    await user.click(toggle);
    expect(preview.lastRender()).toMatchObject({ config: { showFooter: false } });
    await waitFor(() => expect(within(footer).getByText('Footer does not show in the preview now.')).toBeInTheDocument());
    expect(preview.sent).toContainEqual({ type: 'track', target: 'texts.poweredBy', fallbacks: ['behavior.placeholder'] });

    await user.keyboard('{Control>}s{/Control}');
    expect(await screen.findByText('Widget saved.')).toBeInTheDocument();
    expect(lastBody(fetchMock, 'PUT').settings.appearance.showFooter).toBe(false);
  });

  it('puts the panel in a bottom sheet on a narrow window, which opens at half height on a clicked element', async () => {
    // Too narrow for the preview and the panel side by side.
    vi.stubGlobal(
      'ResizeObserver',
      class {
        observe() {}
        unobserve() {}
        disconnect() {}
      },
    );
    const measure = vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockReturnValue(new DOMRect(0, 0, 640, 720));
    onTestFinished(() => measure.mockRestore());
    const user = userEvent.setup();
    mockFetch(routes(() => [shop]));
    renderAt(`/widgets/${shop.id}`);
    const preview = await fakePreview();

    const sheet = screen.getByRole('region', { name: 'Settings' });
    expect(sheet.tagName).toBe('SECTION');
    const handle = within(sheet).getByRole('button', { name: 'Settings' });
    expect(handle).toHaveAttribute('aria-expanded', 'false');

    preview.click('appearance.title');
    await elementView('Title');
    expect(handle).toHaveAttribute('aria-expanded', 'true');
    expect(within(sheet).getByRole('textbox', { name: 'Title' })).toBeInTheDocument();
    expect(screen.queryByRole('dialog')).toBeNull();

    await user.keyboard('{Escape}');
    expect(within(sheet).queryByRole('heading', { level: 2, name: 'Title' })).toBeNull();
    expect(handle).toHaveAttribute('aria-expanded', 'true');
    await user.keyboard('{Escape}');
    expect(handle).toHaveAttribute('aria-expanded', 'false');
    expect(handle).toHaveFocus();

    preview.click('appearance.title');
    await elementView('Title');
    await user.click(handle);
    expect(handle).toHaveAttribute('aria-expanded', 'false');
    expect(within(sheet).queryByRole('heading', { level: 2, name: 'Title' })).toBeNull();
  });

  it('deletes a widget after confirmation', async () => {
    const user = userEvent.setup();
    let widgets = [shop];
    const fetchMock = mockFetch(
      routes(() => widgets, {
        [`DELETE /api/widgets/${shop.id}`]: () => {
          widgets = [];
          return new Response(null, { status: 204 });
        },
      }),
    );
    renderAt('/widgets');

    await user.click(await screen.findByRole('button', { name: 'Actions for Shop help' }));
    await user.click(screen.getByRole('menuitem', { name: 'Delete widget' }));
    const confirm = await screen.findByRole('dialog', { name: 'Delete widget?' });
    expect(confirm).toHaveTextContent('“Shop help” will be deleted with its images.');
    await user.click(within(confirm).getByRole('button', { name: 'Delete' }));

    expect(await screen.findByText('Put a chat on your website')).toBeInTheDocument();
    expect(fetchCalls(fetchMock)).toContain(`DELETE /api/widgets/${shop.id}`);
  });
});

describe('widget form', () => {
  const defaults = widgetOptions().defaults;

  it('accepts site origins with an optional subdomain wildcard', () => {
    expect(isSiteOrigin('https://www.example.com')).toBe(true);
    expect(isSiteOrigin('https://*.example.com')).toBe(true);
    expect(isSiteOrigin('http://localhost:3000/')).toBe(true);
    expect(isSiteOrigin('https://example.com/page')).toBe(false);
    expect(isSiteOrigin('example.com')).toBe(false);
  });

  it('checks numbers, opening hours and the time zone', () => {
    const values = { ...initialWidgetValues(shop, defaults), messagesPerMinute: '0', alwaysAvailable: false, timeZone: 'Mars/Olympus' };
    const { request, errors } = toSaveRequest({ ...values, hours: [{ key: 'a', day: 'monday', opens: '17:00', closes: '09:00' }] }, shop.settings);
    expect(request).toBeNull();
    expect(errors).toEqual({
      'settings.security.messagesPerMinute': 'Enter a number from 1 to 60.',
      'settings.handover.availability.timeZone': 'Enter a time zone such as Europe/Prague or UTC.',
      'settings.handover.availability.hours': 'Each row must close after it opens.',
    });
    expect(validateWidget({ ...values, hours: [] })).toEqual({
      'settings.security.messagesPerMinute': 'Enter a number from 1 to 60.',
      'settings.handover.availability.timeZone': 'Enter a time zone such as Europe/Prague or UTC.',
    });
  });

  it('checks and sends how long a request waits for a person and when quiet conversations close', () => {
    const values = { ...initialWidgetValues(shop, defaults), waitMinutes: '0', aiIdleMinutes: '4', humanIdleHours: '169' };
    expect(validateWidget(values)).toEqual({
      'settings.handover.waitMinutes': 'Enter a number from 1 to 1440.',
      'settings.conversations.aiIdleMinutes': 'Enter a number from 5 to 1440.',
      'settings.conversations.humanIdleHours': 'Enter a number from 1 to 168.',
    });

    const saved = toSaveRequest({ ...values, waitMinutes: '5', closeIdle: false, aiIdleMinutes: '45', humanIdleHours: '48' }, shop.settings).request
      ?.settings;
    expect(saved?.handover.waitMinutes).toBe(5);
    expect(saved?.conversations).toEqual({ closeIdle: false, aiIdleMinutes: 45, humanIdleHours: 48 });
    expect(initialWidgetValues(widgetDto({ settings: normalizeSettings({}) }), defaults)).toMatchObject({
      waitMinutes: '15',
      closeIdle: true,
      aiIdleMinutes: '30',
      humanIdleHours: '24',
    });
  });

  it('sends opening hours with seconds, as the API stores times', () => {
    const values = {
      ...initialWidgetValues(shop, defaults),
      alwaysAvailable: false,
      timeZone: 'Europe/Prague',
      hours: [{ key: 'a', day: 'friday' as const, opens: '09:00', closes: '17:30' }],
    };
    expect(toSaveRequest(values, shop.settings).request?.settings.handover.availability).toEqual({
      always: false,
      timeZone: 'Europe/Prague',
      hours: [{ day: 'friday', opens: '09:00:00', closes: '17:30:00' }],
    });
  });

  it('builds the preview config from the form, keeping saved values for fields that do not validate', () => {
    const values = { ...initialWidgetValues(shop, defaults), brandColor: '#12', launcherAssetId: 'asset-1', texts: { send: ' Go ', close: '' } };
    expect(toPreviewConfig(values, shop.publicKey, shop.settings)).toMatchObject({
      key: shop.publicKey,
      brandColor: '#0f766e',
      launcherIconUrl: '/widget/v1/media/asset-1',
      texts: { send: 'Go' },
    });
  });
});
