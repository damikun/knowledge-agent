import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { Toaster } from '../../components/ui/Toast';
import { alertSettings } from '../../test/alertsApi';
import { embeddingProfile, orchestratorProfile } from '../../test/apiFixtures';
import { emailSettings, integrationsRoutes, slackSettings, workingEmail, workingSlack } from '../../test/integrationsApi';
import { fetchCalls, jsonResponse, mockFetch, problemResponse, renderWithProviders } from '../../test/utils';
import { widgetDto, widgetGuid, widgetSettings, widgetsRoutes } from '../../test/widgetsApi';
import { secretRequest } from '../../components/ui/SecretField';
import { splitList, toSlackRequest } from './integrationForms';
import { NotificationsPage } from './NotificationsPage';
import type { IntegrationsDto } from './types';

afterEach(() => {
  vi.unstubAllGlobals();
});

const emailWidget = widgetDto({ settings: widgetSettings({ behavior: { handoverEnabled: true }, handover: { channel: 'email' } }) });

function lastBody(fetchMock: ReturnType<typeof mockFetch>, route: string): Record<string, unknown> {
  const [method, path] = route.split(' ');
  const call = fetchMock.mock.calls
    .filter(([input, init]) => new URL(String(input), 'http://localhost').pathname === path && (init as RequestInit | undefined)?.method === method)
    .at(-1);
  return JSON.parse((call![1] as RequestInit).body as string) as Record<string, unknown>;
}

function serve(get: () => IntegrationsDto, extra: Record<string, unknown> = {}) {
  return mockFetch({
    ...integrationsRoutes(get),
    ...widgetsRoutes(() => [emailWidget]),
    'GET /api/alerts/settings': alertSettings(),
    'GET /api/models/profiles': [orchestratorProfile('openai'), embeddingProfile()],
    ...extra,
  });
}

function renderPage() {
  return renderWithProviders(
    <>
      <NotificationsPage />
      <Toaster />
    </>,
  );
}

function steps(card: HTMLElement): HTMLElement[] {
  return within(within(card).getByRole('list', { name: /setup$/ })).getAllByRole('listitem');
}

function expectSteps(card: HTMLElement, expected: string[]) {
  const items = steps(card);
  expect(items).toHaveLength(expected.length);
  expected.forEach((text, index) => expect(items[index]).toHaveTextContent(text));
}

describe('NotificationsPage', () => {
  it('holds only what reaches the team: email, Slack and alerts, with no tools for agents or AI models', async () => {
    serve(() => ({ email: workingEmail, slack: slackSettings() }));
    renderPage();

    expect(await screen.findByRole('heading', { level: 1, name: 'Notifications' })).toBeInTheDocument();
    const team = screen.getByRole('region', { name: 'Email, Slack and alerts' });
    expect(await within(team).findByRole('region', { name: 'Email' })).toBeInTheDocument();
    expect(within(team).getByRole('region', { name: 'Slack' })).toBeInTheDocument();
    expect(await within(team).findByRole('region', { name: 'Alerts' })).toBeInTheDocument();
    expect(screen.queryByRole('region', { name: 'MCP servers' })).toBeNull();
    expect(screen.queryByRole('region', { name: 'Models' })).toBeNull();
  });

  it('shows what email and Slack are for, whether they work and what is left to set up', async () => {
    serve(() => ({ email: workingEmail, slack: slackSettings() }));
    renderPage();

    expect(await screen.findByRole('heading', { level: 1, name: 'Notifications' })).toBeInTheDocument();
    const email = await screen.findByRole('region', { name: 'Email' });
    expect(within(email).getByText('On')).toBeInTheDocument();
    expect(within(email).getByText(/brings their replies back into the chat/)).toBeInTheDocument();
    expect(await within(email).findByRole('link', { name: 'Shop help' })).toHaveAttribute('href', `/widgets/${widgetGuid(1)}`);
    expectSteps(email, ['Send email by SMTP, done', 'Read replies by IMAP, done', "Send a widget's requests by email, done"]);
    expect(within(email).getByText('3 of 3 done')).toBeInTheDocument();
    expect(within(email).getByRole('button', { name: 'Settings' })).toBeInTheDocument();

    const slack = screen.getByRole('region', { name: 'Slack' });
    expect(within(slack).getByText('Off')).toBeInTheDocument();
    expect(within(slack).getByText('From configuration')).toBeInTheDocument();
    expect(within(slack).getByText('docs/slack-app-manifest.yml')).toBeInTheDocument();
    expectSteps(slack, [
      'Create the Slack app, to do',
      'Add the bot token and a channel, to do',
      'Add the app-level token for replies, to do',
      "Send a widget's requests to Slack, to do",
    ]);
    expect(within(slack).getByRole('button', { name: 'Set up Slack' })).toBeInTheDocument();
  });

  it('edits email in a side sheet: keeps a saved password left empty, removes one on request and replaces one that is typed', async () => {
    const user = userEvent.setup();
    let integrations: IntegrationsDto = { email: workingEmail, slack: workingSlack };
    const fetchMock = serve(() => integrations, {
      'PUT /api/integrations/email': (_url: URL, init: RequestInit) => {
        const body = JSON.parse(init.body as string) as { imapPassword: string | null };
        integrations = { ...integrations, email: { ...workingEmail, hasImapPassword: body.imapPassword !== '', fromName: 'Help desk' } };
        return jsonResponse(integrations.email);
      },
    });
    renderPage();
    const email = await screen.findByRole('region', { name: 'Email' });

    await user.click(within(email).getByRole('button', { name: 'Settings' }));
    let sheet = screen.getByRole('dialog', { name: 'Email settings' });
    expect(within(sheet).getByRole('textbox', { name: 'SMTP server' })).toHaveValue('smtp.example.com');
    const passwords = within(sheet).getAllByLabelText('Password');
    expect(passwords).toHaveLength(2);
    expect(passwords[0]).toHaveValue('');
    expect(passwords[0]).toHaveAttribute('placeholder', 'Saved');

    const fromName = within(sheet).getByRole('textbox', { name: /^Sender name/ });
    await user.clear(fromName);
    await user.type(fromName, 'Help desk');
    expect(within(sheet).getByRole('textbox', { name: /^Staff domains/ })).toHaveValue('example.com');
    await user.type(within(sheet).getByRole('textbox', { name: /^Staff addresses/ }), 'ada@example.com, grace@example.com');
    await user.click(within(sheet).getAllByRole('button', { name: 'Remove saved password' })[1]!);
    expect(passwords[1]).toBeDisabled();
    await user.click(within(sheet).getByRole('button', { name: 'Save' }));

    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
    expect(await screen.findByText('Email settings saved')).toBeInTheDocument();
    expect(lastBody(fetchMock, 'PUT /api/integrations/email')).toEqual({
      enabled: true,
      smtpHost: 'smtp.example.com',
      smtpPort: 587,
      smtpSecurity: 'auto',
      smtpUser: 'mailer',
      smtpPassword: null,
      fromAddress: 'support@example.com',
      fromName: 'Help desk',
      supportAddress: 'team@example.com',
      replyAddress: 'box@example.com',
      plusAddressing: true,
      imapHost: 'imap.example.com',
      imapPort: 993,
      imapSecurity: 'auto',
      imapUser: 'box@example.com',
      imapPassword: '',
      processedFolder: 'Processed',
      staffAddresses: ['ada@example.com', 'grace@example.com'],
      staffDomains: ['example.com'],
      portalUrl: 'https://support.example.com',
    });

    await user.click(within(email).getByRole('button', { name: 'Settings' }));
    sheet = screen.getByRole('dialog', { name: 'Email settings' });
    expect(within(sheet).getAllByRole('button', { name: 'Remove saved password' })).toHaveLength(1);
    await user.type(within(sheet).getAllByLabelText('Password')[0]!, 'n3w-secret');
    await user.click(within(sheet).getByRole('button', { name: 'Save' }));
    await waitFor(() => expect(lastBody(fetchMock, 'PUT /api/integrations/email').smtpPassword).toBe('n3w-secret'));
    expect(lastBody(fetchMock, 'PUT /api/integrations/email').imapPassword).toBeNull();
  });

  it('checks the email fields and shows the errors of the API next to them', async () => {
    const user = userEvent.setup();
    const fetchMock = serve(() => ({ email: emailSettings(), slack: slackSettings() }), {
      'PUT /api/integrations/email': () =>
        problemResponse({ title: 'One or more validation errors occurred.', errors: { staffDomains: ["'example' is not a domain such as example.com."] } }, 400),
    });
    renderPage();
    const email = await screen.findByRole('region', { name: 'Email' });

    await user.click(within(email).getByRole('button', { name: 'Set up email' }));
    const sheet = screen.getByRole('dialog', { name: 'Email settings' });
    await user.click(within(sheet).getByRole('switch', { name: 'Use email for requests for a person' }));
    await user.click(within(sheet).getByRole('button', { name: 'Save' }));
    expect(within(sheet).getByText('Enter the SMTP server to send email.')).toBeInTheDocument();
    expect(within(sheet).getByRole('textbox', { name: 'SMTP server' })).toHaveFocus();
    expect(within(sheet).getByText('Enter the sender address.')).toBeInTheDocument();
    expect(fetchCalls(fetchMock)).not.toContain('PUT /api/integrations/email');

    await user.type(within(sheet).getByRole('textbox', { name: 'SMTP server' }), 'smtp.example.com');
    await user.type(within(sheet).getByRole('textbox', { name: 'Sender address' }), 'support@example.com');
    await user.type(within(sheet).getByRole('textbox', { name: /^Staff domains/ }), 'example');
    await user.click(within(sheet).getByRole('button', { name: 'Save' }));

    expect(await within(sheet).findByText("'example' is not a domain such as example.com.")).toBeInTheDocument();
    expect(within(sheet).getByRole('textbox', { name: /^Staff domains/ })).toHaveAttribute('aria-invalid', 'true');
    expect(screen.getByRole('dialog', { name: 'Email settings' })).toBeInTheDocument();
  });

  it('saves Slack with a new bot token and keeps the saved app-level token', async () => {
    const user = userEvent.setup();
    let slackNow = slackSettings({ hasAppToken: true, saved: true });
    const fetchMock = serve(() => ({ email: emailSettings(), slack: slackNow }), {
      'PUT /api/integrations/slack': () => {
        slackNow = workingSlack;
        return jsonResponse(workingSlack);
      },
    });
    renderPage();
    const slack = await screen.findByRole('region', { name: 'Slack' });

    await user.click(within(slack).getByRole('button', { name: 'Settings' }));
    const sheet = screen.getByRole('dialog', { name: 'Slack settings' });
    await user.click(within(sheet).getByRole('switch', { name: 'Use Slack for requests for a person' }));
    await user.type(within(sheet).getByLabelText('Bot token'), 'not-a-token');
    await user.type(within(sheet).getByRole('textbox', { name: 'Channel id' }), 'C0123456789');
    await user.click(within(sheet).getByRole('button', { name: 'Save' }));
    expect(within(sheet).getByText('The bot token starts with xoxb-.')).toBeInTheDocument();
    expect(fetchCalls(fetchMock)).not.toContain('PUT /api/integrations/slack');

    await user.clear(within(sheet).getByLabelText('Bot token'));
    await user.type(within(sheet).getByLabelText('Bot token'), 'xoxb-1-2-3');
    await user.click(within(sheet).getByRole('button', { name: 'Save' }));

    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
    expect(await screen.findByText('Slack settings saved')).toBeInTheDocument();
    expect(lastBody(fetchMock, 'PUT /api/integrations/slack')).toEqual({ enabled: true, botToken: 'xoxb-1-2-3', appToken: null, channelId: 'C0123456789' });
    expect(await within(slack).findByText('On')).toBeInTheDocument();
    expect(within(slack).getByText('3 of 4 done')).toBeInTheDocument();
  });

  it('keeps the test buttons off until email and Slack are set up', async () => {
    const fetchMock = serve(() => ({ email: emailSettings(), slack: slackSettings() }));
    renderPage();

    const email = await screen.findByRole('region', { name: 'Email' });
    const testEmail = within(email).getByRole('button', { name: 'Send test email' });
    expect(testEmail).toBeDisabled();
    expect(testEmail).toHaveAttribute('title', 'Set up email first');
    const testSlack = within(screen.getByRole('region', { name: 'Slack' })).getByRole('button', { name: 'Send test message' });
    expect(testSlack).toBeDisabled();
    expect(testSlack).toHaveAttribute('title', 'Set up Slack first');
    expect(fetchCalls(fetchMock)).not.toContain('POST /api/integrations/email/test');
  });

  it('asks before sending a test email, to the support address or one entered, and shows a pass in green', async () => {
    const user = userEvent.setup();
    const fetchMock = serve(() => ({ email: workingEmail, slack: workingSlack }), {
      'POST /api/integrations/email/test': (_url: URL, init: RequestInit) =>
        jsonResponse({
          ok: true,
          message: `A test email went to ${(JSON.parse(init.body as string) as { to: string }).to}.`,
          details: ['Sent through the mail server smtp.example.com.', 'Opened the reply mailbox box@example.com; it holds 2 message(s).'],
        }),
    });
    renderPage();

    const email = await screen.findByRole('region', { name: 'Email' });
    const testButton = within(email).getByRole('button', { name: 'Send test email' });
    expect(testButton).toBeEnabled();
    await user.click(testButton);
    let dialog = screen.getByRole('dialog', { name: 'Send a test email' });
    expect(dialog).toHaveTextContent('Send a test email to team@example.com?');
    expect(within(dialog).getByRole('textbox', { name: /Recipient/ })).toHaveValue('team@example.com');
    await user.click(within(dialog).getByRole('button', { name: 'Cancel' }));
    expect(screen.queryByRole('dialog')).toBeNull();
    expect(fetchCalls(fetchMock)).not.toContain('POST /api/integrations/email/test');

    await user.click(testButton);
    dialog = screen.getByRole('dialog', { name: 'Send a test email' });
    const recipient = within(dialog).getByRole('textbox', { name: /Recipient/ });
    await user.clear(recipient);
    await user.type(recipient, 'not an address');
    await user.click(within(dialog).getByRole('button', { name: 'Send' }));
    expect(within(dialog).getByText('Enter an email address, such as team@example.com.')).toBeInTheDocument();
    expect(recipient).toHaveAttribute('aria-invalid', 'true');
    expect(fetchCalls(fetchMock)).not.toContain('POST /api/integrations/email/test');

    await user.clear(recipient);
    await user.type(recipient, 'ada@example.com');
    expect(dialog).toHaveTextContent('Send a test email to ada@example.com?');
    await user.click(within(dialog).getByRole('button', { name: 'Send' }));

    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
    expect(lastBody(fetchMock, 'POST /api/integrations/email/test')).toEqual({ to: 'ada@example.com' });
    const passed = await within(email).findByRole('alert');
    expect(passed).toHaveAttribute('data-tone', 'success');
    expect(passed).toHaveTextContent('Test passed');
    expect(passed).toHaveTextContent('A test email went to ada@example.com.');
    expect(passed).toHaveTextContent('Opened the reply mailbox box@example.com');
    expect(within(passed).queryByRole('button')).toBeNull();
  });

  it('sends no recipient when the field is empty, so the test only signs in', async () => {
    const user = userEvent.setup();
    const fetchMock = serve(() => ({ email: { ...workingEmail, supportAddress: null }, slack: workingSlack }), {
      'POST /api/integrations/email/test': { ok: true, message: 'Connected to the mail server; enter an address to receive a test email' },
    });
    renderPage();

    const email = await screen.findByRole('region', { name: 'Email' });
    await user.click(within(email).getByRole('button', { name: 'Send test email' }));
    const dialog = screen.getByRole('dialog', { name: 'Send a test email' });
    expect(within(dialog).getByRole('textbox', { name: /Recipient/ })).toHaveValue('');
    expect(dialog).toHaveTextContent('the test only signs in to the mail server and sends nothing');
    await user.click(within(dialog).getByRole('button', { name: 'Send' }));

    expect(await within(email).findByRole('alert')).toHaveTextContent('enter an address to receive a test email');
    expect(lastBody(fetchMock, 'POST /api/integrations/email/test')).toEqual({ to: null });
  });

  it('shows a failed test in red with the reason and where to fix it', async () => {
    const user = userEvent.setup();
    serve(() => ({ email: { ...workingEmail, enabled: false, canSend: false, canReceive: false }, slack: workingSlack }), {
      'POST /api/integrations/email/test': { ok: false, message: 'Email is turned off: turn it on in the email settings and save.' },
      'POST /api/integrations/slack/test': { ok: false, message: 'Signed in, but the test message to C0123456789 was not posted: not_in_channel.' },
    });
    renderPage();

    const slack = await screen.findByRole('region', { name: 'Slack' });
    await user.click(within(slack).getByRole('button', { name: 'Send test message' }));
    const dialog = screen.getByRole('dialog', { name: 'Send a test message' });
    expect(dialog).toHaveTextContent('Send a test message to the channel C0123456789?');
    await user.click(within(dialog).getByRole('button', { name: 'Send' }));

    const failed = await within(slack).findByRole('alert');
    expect(failed).toHaveAttribute('data-tone', 'danger');
    expect(failed).toHaveTextContent('Test failed');
    expect(failed).toHaveTextContent('not_in_channel');
    await user.click(within(failed).getByRole('button', { name: 'Open Slack settings' }));
    expect(screen.getByRole('dialog', { name: 'Slack settings' })).toBeInTheDocument();
    await user.click(within(screen.getByRole('dialog', { name: 'Slack settings' })).getByRole('button', { name: 'Cancel' }));

    const email = screen.getByRole('region', { name: 'Email' });
    await user.click(within(email).getByRole('button', { name: 'Send test email' }));
    await user.click(within(screen.getByRole('dialog', { name: 'Send a test email' })).getByRole('button', { name: 'Send' }));
    const emailFailed = await within(email).findByRole('alert');
    expect(emailFailed).toHaveAttribute('data-tone', 'danger');
    expect(emailFailed).toHaveTextContent('Email is turned off');
    expect(within(emailFailed).getByRole('button', { name: 'Open email settings' })).toBeInTheDocument();
  });
});

describe('Alerts card', () => {
  it('saves each change at once: Slack for new alerts, the kinds and the waiting time', async () => {
    const user = userEvent.setup();
    const fetchMock = serve(() => ({ email: emailSettings(), slack: workingSlack }), {
      'PUT /api/alerts/settings': (_url: URL, init: RequestInit) => jsonResponse({ ...alertSettings(), ...(JSON.parse(init.body as string) as object) }),
    });
    renderPage();

    const card = await screen.findByRole('region', { name: 'Alerts' });
    expect(within(card).getByText('Off')).toBeInTheDocument();
    expect(within(card).getByText('To C0123456789, the channel for requests.')).toBeInTheDocument();
    expect(within(card).getByText('Set up email with a support address above first.')).toBeInTheDocument();
    expect(within(card).queryByRole('button', { name: /Save/ })).toBeNull();

    await user.click(within(card).getByRole('switch', { name: 'Post to Slack' }));
    expect(within(card).getByText('Slack')).toBeInTheDocument();
    await waitFor(() => expect(lastBody(fetchMock, 'PUT /api/alerts/settings')).toMatchObject({ slack: true, email: false }));

    await user.click(within(card).getByRole('switch', { name: 'Ratings dropped' }));
    await user.click(within(card).getByRole('switch', { name: 'Fewer thumbs up' }));
    await user.click(within(card).getByRole('combobox', { name: 'A visitor waiting longer than' }));
    await user.click(await screen.findByRole('option', { name: '10 minutes' }));

    await waitFor(() => expect(lastBody(fetchMock, 'PUT /api/alerts/settings')).toMatchObject({ slack: true, email: false, waitingMinutes: 10 }));
    expect(lastBody(fetchMock, 'PUT /api/alerts/settings').kinds).toEqual(
      alertSettings().kinds.filter((kind) => kind !== 'rating_drop' && kind !== 'thumbs_drop'),
    );
    expect(await within(card).findByText('Saved')).toBeInTheDocument();
  });

  it('keeps a waiting time that is not one of the choices', async () => {
    serve(() => ({ email: workingEmail, slack: workingSlack }), { 'GET /api/alerts/settings': alertSettings({ waitingMinutes: 7 }) });
    renderPage();

    const card = await screen.findByRole('region', { name: 'Alerts' });
    expect(within(card).getByRole('combobox', { name: 'A visitor waiting longer than' })).toHaveTextContent('7 minutes');
  });
});

describe('integration forms', () => {
  it('sends null to keep a secret, an empty string to remove it and the value to replace it', () => {
    expect(secretRequest({ value: '', remove: false })).toBeNull();
    expect(secretRequest({ value: '  ', remove: false })).toBeNull();
    expect(secretRequest({ value: '', remove: true })).toBe('');
    expect(secretRequest({ value: ' s3cret ', remove: false })).toBe('s3cret');
  });

  it('splits lists on new lines and commas', () => {
    expect(splitList('a@example.com\n b@example.com, ,c@example.com\n')).toEqual(['a@example.com', 'b@example.com', 'c@example.com']);
  });

  it('needs a bot token and a channel to turn Slack on', () => {
    const values = { enabled: true, botToken: { value: '', remove: false }, appToken: { value: '', remove: false }, channelId: '' };
    expect(toSlackRequest(values, slackSettings()).errors).toEqual({ botToken: 'Slack needs the bot token.', channelId: 'Slack needs a channel.' });
    expect(toSlackRequest({ ...values, channelId: 'C0123456789' }, slackSettings({ hasBotToken: true })).request).toEqual({
      enabled: true,
      botToken: null,
      appToken: null,
      channelId: 'C0123456789',
    });
  });
});
