import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { fetchCalls, jsonResponse, mockFetch, problemResponse, renderWithProviders } from '../../test/utils';
import { Toaster } from '../../components/ui/Toast';
import { PromptsPage } from './PromptsPage';
import type { PromptDetailDto, PromptSummaryDto, PromptVersionDto, PromptVersionsDto } from './types';

afterEach(() => {
  vi.unstubAllGlobals();
});

const NAME = 'conversation-title';

const summaries: PromptSummaryDto[] = [{ name: NAME, origin: 'langfuse', version: 4, type: 'text', labels: ['production'], variables: ['message'], externalUrl: 'http://localhost:3000/p' }];

const detail: PromptDetailDto = {
  name: NAME,
  origin: 'langfuse',
  version: 4,
  type: 'text',
  text: 'Write a title for:\n{{message}}',
  messages: [],
  config: null,
  labels: ['production'],
  variables: ['message'],
  externalUrl: 'http://localhost:3000/p',
};

function version(number: number, text: string, live = false, commitMessage: string | null = null): PromptVersionDto {
  return { version: number, type: 'text', text, messages: [], config: null, labels: live ? ['production'] : [], commitMessage, live };
}

const versions: PromptVersionsDto = {
  name: NAME,
  editable: true,
  label: 'production',
  liveVersion: 4,
  totalVersions: 3,
  versions: [version(5, 'Write a short title for:\n{{message}}', false, 'Shorter (by Ada)'), version(4, detail.text!, true), version(3, 'Title:\n{{message}}')],
};

// Echoes the template with {{message}} filled in and reports variables like the API.
function renderTemplate(_url: URL, init: RequestInit) {
  const body = JSON.parse(String(init.body)) as { template: string; variables: Record<string, unknown> };
  const used = [...body.template.matchAll(/\{\{\s*([a-zA-Z]+)\s*\}\}/g)].map((match) => match[1]!);
  return jsonResponse({ output: body.template.replace('{{message}}', String(body.variables.message ?? '')), variables: [...new Set(used)].sort() });
}

function bodies(fetchMock: ReturnType<typeof mockFetch>, route: string): unknown[] {
  const [method, path] = route.split(' ');
  return fetchMock.mock.calls
    .filter(([input, init]) => {
      const url = new URL(typeof input === 'string' ? input : (input as URL).href, 'http://localhost');
      return url.pathname === path && ((init as RequestInit | undefined)?.method ?? 'GET').toUpperCase() === method;
    })
    .map(([, init]) => JSON.parse(String((init as RequestInit).body)));
}

describe('editing prompts', () => {
  it('edits beside a live preview and saves a draft that stays off until published', async () => {
    const user = userEvent.setup();
    const fetchMock = mockFetch({
      'GET /api/prompts': summaries,
      [`GET /api/prompts/${NAME}`]: detail,
      'POST /api/prompts/render': renderTemplate,
      [`POST /api/prompts/${NAME}/versions`]: () => jsonResponse(version(5, 'Write a short title for {{message}} in {{language}}', false, 'Shorter (by Local admin)'), 201),
      [`GET /api/prompts/${NAME}/versions`]: versions,
    });
    renderWithProviders(<><PromptsPage /><Toaster /></>, { route: `/prompts/${NAME}`, path: '/prompts/:name?' });

    const region = await screen.findByRole('region', { name: 'Conversation titles' });
    expect(within(region).getByRole('link', { name: /Edit in Langfuse/ })).toBeInTheDocument();
    await user.click(within(region).getByRole('button', { name: 'Edit' }));

    const template = screen.getByRole('textbox', { name: 'Template' });
    expect(template).toHaveValue(detail.text);
    expect(screen.getByRole('button', { name: 'Save as draft' })).toBeDisabled();

    await user.clear(template);
    await user.type(template, 'Write a short title for {{{{message}} in {{{{language}}');
    await waitFor(() => expect(screen.getByTestId('edit-preview')).toHaveTextContent('Write a short title for'));
    const used = await screen.findByRole('list', { name: 'Variables in the edited template' });
    await waitFor(() => expect(within(used).getAllByRole('listitem').map((item) => item.textContent)).toEqual(['language (no sample value)', 'message']));

    await user.type(screen.getByRole('textbox', { name: /What changed/ }), 'Shorter');
    await user.click(screen.getByRole('button', { name: 'Save as draft' }));

    expect(await screen.findByText('Saved version 5 as a draft')).toBeInTheDocument();
    expect(bodies(fetchMock, `POST /api/prompts/${NAME}/versions`)).toEqual([
      { text: 'Write a short title for {{message}} in {{language}}', commitMessage: 'Shorter', baseVersion: 4 },
    ]);
    expect(fetchCalls(fetchMock)).not.toContain(`POST /api/prompts/${NAME}/publish`);
    expect(await screen.findByRole('tab', { name: 'Versions', selected: true })).toBeInTheDocument();
    expect(await screen.findByRole('region', { name: 'Version 5' })).toBeInTheDocument();
  });

  it('publishes an edit after a confirmation, against the live version it started from', async () => {
    const user = userEvent.setup();
    const fetchMock = mockFetch({
      'GET /api/prompts': summaries,
      [`GET /api/prompts/${NAME}`]: detail,
      'POST /api/prompts/render': renderTemplate,
      [`POST /api/prompts/${NAME}/versions`]: () => jsonResponse(version(5, 'New {{message}}'), 201),
      [`POST /api/prompts/${NAME}/publish`]: { name: NAME, label: 'production', version: 5, previousVersion: 4 },
    });
    renderWithProviders(<><PromptsPage /><Toaster /></>, { route: `/prompts/${NAME}`, path: '/prompts/:name?' });

    await user.click(await screen.findByRole('button', { name: 'Edit' }));
    const template = screen.getByRole('textbox', { name: 'Template' });
    await user.clear(template);
    await user.type(template, 'New {{{{message}}');
    await user.click(screen.getByRole('button', { name: 'Publish' }));

    const dialog = screen.getByRole('dialog', { name: 'Publish this version?' });
    expect(fetchCalls(fetchMock)).not.toContain(`POST /api/prompts/${NAME}/versions`);
    await user.click(within(dialog).getByRole('button', { name: 'Publish' }));

    expect(await screen.findByText('Version 5 is live')).toBeInTheDocument();
    expect(bodies(fetchMock, `POST /api/prompts/${NAME}/publish`)).toEqual([{ version: 5, expectedLiveVersion: 4 }]);
    expect(screen.queryByRole('textbox', { name: 'Template' })).toBeNull();
  });

  it('shows a template error from the API beside the template and keeps the edit', async () => {
    const user = userEvent.setup();
    mockFetch({
      'GET /api/prompts': summaries,
      [`GET /api/prompts/${NAME}`]: detail,
      'POST /api/prompts/render': renderTemplate,
      [`POST /api/prompts/${NAME}/versions`]: () => problemResponse({ title: 'Validation failed', errors: { text: ['The block {{#if}} is never closed.'] } }, 400),
    });
    renderWithProviders(<><PromptsPage /><Toaster /></>, { route: `/prompts/${NAME}`, path: '/prompts/:name?' });

    await user.click(await screen.findByRole('button', { name: 'Edit' }));
    const template = screen.getByRole('textbox', { name: 'Template' });
    await user.type(template, '{{{{#if open}}');
    await user.click(screen.getByRole('button', { name: 'Save as draft' }));

    expect(await screen.findByText('The block {{#if}} is never closed.')).toBeInTheDocument();
    expect(template).toHaveAttribute('aria-invalid', 'true');
    expect(template).toHaveValue(`${detail.text}{{#if open}}`);
  });

  it('compares a version with the live one and rolls back after a confirmation', async () => {
    const user = userEvent.setup();
    const fetchMock = mockFetch({
      'GET /api/prompts': summaries,
      [`GET /api/prompts/${NAME}`]: detail,
      [`GET /api/prompts/${NAME}/versions`]: versions,
      [`POST /api/prompts/${NAME}/publish`]: { name: NAME, label: 'production', version: 3, previousVersion: 4 },
    });
    renderWithProviders(<><PromptsPage /><Toaster /></>, { route: `/prompts/${NAME}`, path: '/prompts/:name?' });

    await user.click(await screen.findByRole('tab', { name: 'Versions' }));
    const draft = await screen.findByRole('region', { name: 'Version 5' });
    const diff = within(draft).getByLabelText('Changes against the live version');
    expect(within(diff).getByText('Write a short title for:')).toBeInTheDocument();
    expect(within(diff).getByText('Write a title for:')).toBeInTheDocument();
    expect(within(draft).getByRole('button', { name: 'Publish version 5' })).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: /Version 3/ }));
    const older = screen.getByRole('region', { name: 'Version 3' });
    await user.click(within(older).getByRole('button', { name: 'Roll back to version 3' }));
    const dialog = screen.getByRole('dialog', { name: 'Roll back to version 3?' });
    await user.click(within(dialog).getByRole('button', { name: 'Roll back' }));

    expect(await screen.findByText('Rolled back to version 3')).toBeInTheDocument();
    expect(bodies(fetchMock, `POST /api/prompts/${NAME}/publish`)).toEqual([{ version: 3, expectedLiveVersion: 4 }]);
  });

  it('offers no editing for the built-in defaults', async () => {
    mockFetch({
      'GET /api/prompts': [{ ...summaries[0]!, origin: 'local', externalUrl: null }],
      [`GET /api/prompts/${NAME}`]: { ...detail, origin: 'local', externalUrl: null },
    });
    renderWithProviders(<><PromptsPage /><Toaster /></>, { route: `/prompts/${NAME}`, path: '/prompts/:name?' });

    const region = await screen.findByRole('region', { name: 'Conversation titles' });
    expect(within(region).queryByRole('button', { name: 'Edit' })).toBeNull();
    expect(within(region).queryByRole('tab', { name: 'Versions' })).toBeNull();
  });
});
