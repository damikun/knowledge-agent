import { act, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { fetchCalls, jsonResponse, mockFetch, problemResponse, renderWithProviders } from '../../test/utils';
import { tokenizeTemplate } from './handlebars';
import { parseVariables } from './json';
import { PromptsPage } from './PromptsPage';
import { guessSampleValue, sampleVariables } from './samples';
import type { PromptDetailDto, PromptSummaryDto } from './types';

afterEach(() => {
  vi.useRealTimers();
  vi.unstubAllGlobals();
});

const TEMPLATE = 'You are {{agentName}}.\n{{#if sources}}{{#each sources}}- {{title}}\n{{/each}}{{else}}No sources.{{/if}}\n{{!-- internal note --}}';

const summaries: PromptSummaryDto[] = [
  { name: 'orchestrator-system', origin: 'langfuse', version: 4, type: 'text', labels: ['production'], variables: ['agentName', 'sources'], externalUrl: 'http://localhost:3000/project/p/prompts/orchestrator-system' },
  { name: 'conversation-title', origin: 'local', version: 1, type: 'chat', labels: [], variables: ['message'], externalUrl: null },
];

const detail: PromptDetailDto = {
  name: 'orchestrator-system',
  origin: 'langfuse',
  version: 4,
  type: 'text',
  text: TEMPLATE,
  messages: [],
  config: { reasoningEffort: 'high' },
  labels: ['production'],
  variables: ['agentName', 'sources'],
  externalUrl: 'http://localhost:3000/project/p/prompts/orchestrator-system',
};

describe('PromptsPage', () => {
  it('lists the prompts by purpose, selects the first and previews it live with sample variables', async () => {
    const fetchMock = mockFetch({
      'GET /api/prompts': summaries,
      'GET /api/prompts/orchestrator-system': detail,
      'POST /api/prompts/orchestrator-system/render': () =>
        jsonResponse({ name: 'orchestrator-system', origin: 'langfuse', version: 4, type: 'text', text: 'You are orchestrator.\n- Employee handbook 2026', messages: [], config: detail.config, profile: null, modelOverride: { provider: 'auto', model: null, temperature: null, maxOutputTokens: null, reasoningEffort: 'high', reasoningOutput: null, dimensions: null } }),
    });
    renderWithProviders(<PromptsPage />, { route: '/prompts', path: '/prompts/:name?' });

    const prompt = await screen.findByRole('region', { name: 'Answering agent' });
    const list = screen.getByRole('navigation', { name: 'Prompts' });
    expect(within(list).getAllByRole('list')).toHaveLength(2);
    expect(within(within(list).getByRole('list', { name: 'Answering' })).getByRole('link', { name: /^Answering agent/ })).toHaveAttribute('aria-current', 'page');
    expect(within(within(list).getByRole('list', { name: 'Conversations' })).getByRole('link', { name: /^Conversation titles/ })).toHaveAttribute(
      'href',
      '/prompts/conversation-title',
    );
    expect(within(prompt).getByText('orchestrator-system')).toBeInTheDocument();
    expect(within(prompt).getByRole('link', { name: /Edit in Langfuse/ })).toHaveAttribute('href', detail.externalUrl);

    const template = screen.getByLabelText('Prompt template');
    expect(within(template).getByText('{{agentName}}')).toHaveAttribute('data-token', 'variable');
    expect(within(template).getByText('{{#if sources}}')).toHaveAttribute('data-token', 'block-open');
    expect(within(template).getByText('{{!-- internal note --}}')).toHaveAttribute('data-token', 'comment');
    const user = userEvent.setup();
    await user.click(within(prompt).getByRole('tab', { name: 'Variables and config' }));
    expect(screen.getByText(/"reasoningEffort": "high"/)).toBeVisible();
    expect(within(screen.getByRole('list', { name: 'Detected variables' })).getAllByRole('listitem')).toHaveLength(2);
    expect(screen.getByLabelText('Prompt template')).not.toBeVisible();

    await user.click(within(prompt).getByRole('tab', { name: 'Live preview' }));
    const editor = screen.getByRole('textbox', { name: 'Sample variables' });
    const prefilled = JSON.parse((editor as HTMLTextAreaElement).value);
    expect(prefilled).toMatchObject({ agentName: 'Knowledge Agent', appName: 'Knowledge Agent', researcherEnabled: true, sourceCount: 3 });

    expect(await screen.findByTestId('rendered-output')).toHaveTextContent('You are orchestrator. - Employee handbook 2026');
    expect(screen.getByText(/Prompt config: effort high/)).toBeInTheDocument();
    const renderCall = fetchMock.mock.calls.find(([url]) => String(url).endsWith('/render'))!;
    expect(JSON.parse((renderCall[1] as RequestInit).body as string).variables).toEqual(prefilled);
  });

  it('renders again when the variables change, but not while they are not a JSON object', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime });
    const bodies: unknown[] = [];
    const fetchMock = mockFetch({
      'GET /api/prompts': summaries,
      'GET /api/prompts/orchestrator-system': detail,
      'POST /api/prompts/orchestrator-system/render': (_url: URL, init: RequestInit) => {
        const { variables } = JSON.parse(init.body as string) as { variables: { agentName?: string } };
        bodies.push(variables);
        return jsonResponse({ ...detail, text: `You are ${variables.agentName ?? 'nobody'}.`, profile: null, modelOverride: null });
      },
    });
    const renders = () => fetchCalls(fetchMock).filter((call) => call === 'POST /api/prompts/orchestrator-system/render').length;
    renderWithProviders(<PromptsPage />, { route: '/prompts/orchestrator-system', path: '/prompts/:name?' });
    await user.click(await screen.findByRole('tab', { name: 'Live preview' }));
    const editor = await screen.findByRole('textbox', { name: 'Sample variables' });
    expect(await screen.findByTestId('rendered-output')).toHaveTextContent('You are Knowledge Agent.');

    await user.clear(editor);
    await user.click(editor);
    await user.paste('[1, 2]');
    expect(screen.getByText(/Variables must be a JSON object/)).toBeInTheDocument();
    await act(() => vi.advanceTimersByTimeAsync(1000));
    expect(renders()).toBe(1);
    expect(screen.getByTestId('rendered-output')).toHaveTextContent('You are Knowledge Agent.');

    await user.clear(editor);
    await user.paste('{ "agentName": "Ada" }');
    await act(() => vi.advanceTimersByTimeAsync(1000));
    await waitFor(() => expect(screen.getByTestId('rendered-output')).toHaveTextContent('You are Ada.'));
    expect(bodies.at(-1)).toEqual({ agentName: 'Ada' });
  });

  it('renders ad-hoc templates live in the playground and shows template errors', async () => {
    const user = userEvent.setup();
    mockFetch({
      'GET /api/prompts': summaries,
      'POST /api/prompts/render': (_url: URL, init: RequestInit) => {
        const { template } = JSON.parse(init.body as string) as { template: string };
        if (template.includes('{{#if}}')) {
          return problemResponse({ title: 'Template error', detail: 'Parse error: {{#if}} is not closed.' }, 400);
        }
        return jsonResponse({ output: template === 'Hello {{name}}!' ? 'Hello Ada!' : 'Example output', variables: ['name'] });
      },
    });
    renderWithProviders(<PromptsPage />, { route: '/prompts?view=playground', path: '/prompts/:name?' });

    expect(await screen.findByRole('tab', { name: 'Playground', selected: true })).toBeInTheDocument();
    expect(screen.getByText('Handlebars cheat sheet')).toBeInTheDocument();
    expect(screen.getByText('{{#each items}}{{@index}}. {{title}}{{/each}}')).toBeInTheDocument();

    expect(await screen.findByTestId('rendered-output')).toHaveTextContent('Example output');

    const template = screen.getByRole('textbox', { name: 'Template' });
    await user.clear(template);
    await user.type(template, 'Hello {{{{name}}!');
    expect(template).toHaveValue('Hello {{name}}!');
    await waitFor(() => expect(screen.getByTestId('rendered-output')).toHaveTextContent('Hello Ada!'));

    await user.type(template, ' {{{{#if}}');
    expect(await screen.findByText('Parse error: {{#if}} is not closed.')).toBeInTheDocument();
    expect(screen.queryByTestId('rendered-output')).toBeNull();
  });

  it('shows the list first on a narrow window and the detail with a way back', async () => {
    const user = userEvent.setup();
    vi.spyOn(window, 'matchMedia').mockImplementation(
      (query: string) =>
        ({ matches: query.includes('max-width'), media: query, addEventListener: () => undefined, removeEventListener: () => undefined }) as unknown as MediaQueryList,
    );
    mockFetch({ 'GET /api/prompts': summaries, 'GET /api/prompts/orchestrator-system': detail });
    renderWithProviders(<PromptsPage />, { route: '/prompts', path: '/prompts/:name?' });

    const list = await screen.findByRole('navigation', { name: 'Prompts' });
    expect(screen.queryByRole('region', { name: 'Answering agent' })).toBeNull();

    await user.click(within(list).getByRole('link', { name: /^Answering agent/ }));
    expect(await screen.findByRole('region', { name: 'Answering agent' })).toBeInTheDocument();
    expect(screen.queryByRole('navigation', { name: 'Prompts' })).toBeNull();

    await user.click(screen.getByRole('link', { name: 'All prompts' }));
    expect(await screen.findByRole('navigation', { name: 'Prompts' })).toBeInTheDocument();
    vi.restoreAllMocks();
  });

  it('switches between catalog and playground via the tabs', async () => {
    const user = userEvent.setup();
    mockFetch({ 'GET /api/prompts': summaries, 'GET /api/prompts/orchestrator-system': detail });
    renderWithProviders(<PromptsPage />, { route: '/prompts/orchestrator-system', path: '/prompts/:name?' });
    await user.click(await screen.findByRole('tab', { name: 'Playground' }));
    await waitFor(() => expect(screen.getByRole('textbox', { name: 'Template' })).toBeInTheDocument());
    await user.click(screen.getByRole('tab', { name: 'Prompts' }));
    expect(await screen.findByLabelText('Prompt template')).toBeInTheDocument();
  });
});

describe('prompt helpers', () => {
  it('tokenizes Handlebars tags', () => {
    expect(tokenizeTemplate('Hi {{name}}{{#if x}}!{{else}}?{{/if}}{{{raw}}}').map((t) => t.type)).toEqual([
      'text',
      'variable',
      'block-open',
      'text',
      'else',
      'text',
      'block-close',
      'variable',
    ]);
  });

  it('parses variables JSON', () => {
    expect(parseVariables('')).toEqual({ ok: true, value: {} });
    expect(parseVariables('{"a":1}')).toEqual({ ok: true, value: { a: 1 } });
    expect(parseVariables('{').ok).toBe(false);
    expect(parseVariables('null').ok).toBe(false);
  });

  it('builds sample variables for known prompts and guesses the rest', () => {
    const samples = sampleVariables('orchestrator-system', ['agentName', 'customerTier']);
    expect(samples).toMatchObject({ agentName: 'Knowledge Agent', sourceCount: 3, pendingSourceCount: 0, allowGeneralKnowledge: true, language: 'English', customerTier: 'example customerTier' });
    expect(samples).not.toHaveProperty('sources');
    expect(Array.isArray(samples.sourceTypes)).toBe(true);
    expect(sampleVariables('conversation-title', [])).toHaveProperty('message');
    expect(guessSampleValue('maxSources')).toBe(3);
    expect(guessSampleValue('isAdmin')).toBe(true);
    expect(guessSampleValue('userQuestion')).toMatch(/vacation/);
  });

  it('has a sample for the eval judge with passages the answer cites', () => {
    const samples = sampleVariables('eval-judge', ['question', 'answer', 'answerable', 'referenceAnswer', 'expectedFacts', 'passages']);
    expect(Object.keys(samples).sort()).toEqual(['answer', 'answerable', 'expectedFacts', 'passages', 'question', 'referenceAnswer']);
    expect(samples).toMatchObject({ question: expect.stringMatching(/vacation/), answerable: true, referenceAnswer: expect.any(String) });
    expect(samples.answer).toMatch(/\[S1\].*\[S2\]/);
    expect(samples.expectedFacts).toEqual([expect.any(String), expect.any(String)]);
    expect(samples.passages).toEqual([
      { ref: 'S1', title: 'Employee handbook 2026', text: expect.stringMatching(/25 days/) },
      { ref: 'S2', title: 'Leave policy', text: expect.stringMatching(/line manager/) },
    ]);
  });
});
