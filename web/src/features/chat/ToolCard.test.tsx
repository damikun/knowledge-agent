import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { citationS1, ev, sourceDto } from '../../test/fixtures';
import { sourceGuid, sourcesRoutes } from '../../test/sourcesApi';
import { fetchCalls, mockFetch, renderWithProviders, requestUrls } from '../../test/utils';
import { chatReducer, createChatState, type ToolCallView } from './chatReducer';
import { SourceTitleContext } from './citationContext';
import { ChatDisplayContext } from './chatDisplay';
import { ToolActivity, ToolStep } from './ToolCard';
import type { ChatEvent } from './types';

function toolsFrom(events: ChatEvent[]): ToolCallView[] {
  let state = chatReducer(createChatState(), { type: 'send', id: 'l', message: 'q', at: 0 });
  for (const event of events) {
    state = chatReducer(state, { type: 'event', event, at: 1 });
  }
  return state.turns[0]!.tools;
}

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('ToolActivity', () => {
  it('shows one quiet line with the live step while a tool runs', () => {
    const tools = toolsFrom([ev('tool-call', { callId: 'c1', toolName: 'search_knowledge_base', arguments: { query: 'vacation days', top_k: 5 } })]);
    renderWithProviders(<ToolActivity tools={tools} />);
    const toggle = screen.getByRole('button', { name: /Searching the knowledge base…/ });
    expect(toggle).toHaveTextContent('“vacation days”');
    expect(toggle).toHaveAttribute('aria-expanded', 'false');
    expect(screen.queryByTestId('tool-card')).toBeNull();
  });

  it("shows the agent's note before the tools while they run, and above its step when opened", async () => {
    const user = userEvent.setup();
    const events = [
      ev('text-delta', { text: "I'll check the handbook.", step: 1 }),
      ev('step-finished', { step: 1, kind: 'interim' }),
      ev('tool-call', { callId: 'c1', toolName: 'search_knowledge_base', arguments: { query: 'vacation days' } }),
    ];
    const { rerender } = renderWithProviders(<ToolActivity tools={toolsFrom(events)} />);
    expect(screen.getByRole('button', { name: /I'll check the handbook\./ })).not.toHaveTextContent('vacation days');

    const done = toolsFrom([
      ...events,
      ev('tool-result', {
        callId: 'c1',
        toolName: 'search_knowledge_base',
        isError: false,
        summary: 'Found 1 passage',
        durationMs: 5,
        citations: [citationS1],
      }),
    ]);
    rerender(<ToolActivity tools={done} />);
    await user.click(screen.getByRole('button', { name: /Searched the knowledge base/ }));
    expect(within(screen.getByTestId('tool-card')).getByTestId('tool-preamble')).toHaveTextContent("I'll check the handbook.");
  });

  it('sums up finished steps with the passages they found and opens the steps on demand', async () => {
    const user = userEvent.setup();
    const tools = toolsFrom([
      ev('tool-call', { callId: 'c1', toolName: 'search_knowledge_base', arguments: { query: 'vacation days' } }),
      ev('tool-result', {
        callId: 'c1',
        toolName: 'search_knowledge_base',
        isError: false,
        summary: 'Found 1 passage in 1 source',
        durationMs: 412,
        citations: [citationS1],
      }),
      ev('tool-call', { callId: 'c2', toolName: 'search_knowledge_base', arguments: { query: 'carry over' } }),
      ev('tool-result', {
        callId: 'c2',
        toolName: 'search_knowledge_base',
        isError: false,
        summary: 'Found 1 passage',
        durationMs: 100,
        citations: [citationS1],
      }),
    ]);
    renderWithProviders(<ToolActivity tools={tools} />);

    const toggle = screen.getByRole('button', { name: /Searched the knowledge base 2 times · 1 passage/ });
    expect(screen.queryByText('412 ms')).toBeNull();
    await user.click(toggle);
    expect(toggle).toHaveAttribute('aria-expanded', 'true');
    const [first] = screen.getAllByTestId('tool-card');
    expect(within(first!).getByText('“vacation days”')).toBeInTheDocument();
    const passages = within(first!).getByRole('list', { name: 'Passages' });
    expect(within(passages).getByText('Employee handbook')).toBeInTheDocument();
    expect(within(passages).getByText(citationS1.snippet)).toBeInTheDocument();
    expect(within(first!).queryByText('Arguments')).toBeNull();
    expect(within(first!).queryByText('0.92')).toBeNull();
  });

  it('shows scores, timings and arguments to admins', async () => {
    const user = userEvent.setup();
    const tools = toolsFrom([
      ev('tool-call', { callId: 'c1', toolName: 'search_knowledge_base', arguments: { query: 'vacation days' } }),
      ev('tool-result', {
        callId: 'c1',
        toolName: 'search_knowledge_base',
        isError: false,
        summary: 'Found 1 passage',
        durationMs: 412,
        citations: [citationS1],
      }),
    ]);
    renderWithProviders(
      <ChatDisplayContext.Provider value={{ developer: true }}>
        <ToolActivity tools={tools} />
      </ChatDisplayContext.Provider>,
    );
    await user.click(screen.getByRole('button', { name: /Searched the knowledge base/ }));
    const step = screen.getByTestId('tool-card');
    expect(within(step).getByText('412 ms')).toBeInTheDocument();
    expect(within(step).getByText(`S1 · ${citationS1.score.toFixed(2)}`)).toBeInTheDocument();
    expect(within(step).getByText('Arguments')).toBeInTheDocument();
  });

  it('counts failed steps in the line', () => {
    const tools = toolsFrom([
      ev('tool-call', { callId: 'c1', toolName: 'read_source', arguments: { source_id: 'src-handbook' } }),
      ev('tool-result', { callId: 'c1', toolName: 'read_source', isError: true, summary: 'Source not found', durationMs: 3, citations: [] }),
    ]);
    renderWithProviders(<ToolActivity tools={tools} />);
    expect(screen.getByRole('button', { name: /Read a source · 1 step failed/ })).toBeInTheDocument();
  });
});

describe('ToolStep', () => {
  it('renders the error state with the error summary', () => {
    const [tool] = toolsFrom([
      ev('tool-call', { callId: 'c1', toolName: 'read_source', arguments: { source_id: 'src-handbook' } }),
      ev('tool-result', { callId: 'c1', toolName: 'read_source', isError: true, summary: 'Source not found', durationMs: 3, citations: [] }),
    ]);
    renderWithProviders(
      <SourceTitleContext.Provider value={(id) => (id === 'src-handbook' ? 'Employee handbook' : undefined)}>
        <ToolStep tool={tool!} />
      </SourceTitleContext.Provider>,
    );
    const card = screen.getByTestId('tool-card');
    expect(card).toHaveAttribute('data-status', 'error');
    expect(within(card).getByText('Read a source')).toBeInTheDocument();
    expect(within(card).getByText('Employee handbook')).toBeInTheDocument();
    expect(within(card).getByText('Source not found')).toHaveAttribute('data-tone', 'danger');
    expect(within(card).getByText(/failed/)).toHaveClass('sr-only');
  });

  it('nests researcher activity inside the delegate_research step while it runs', () => {
    const [tool] = toolsFrom([
      ev('tool-call', { callId: 'd1', toolName: 'delegate_research', arguments: { task: 'Check carry-over rules' } }),
      ev('agent-started', { agent: 'researcher', parentCallId: 'd1', provider: 'anthropic', model: 'claude-opus-5-5' }),
      ev('tool-call', {
        agent: 'researcher',
        parentCallId: 'd1',
        callId: 'r1',
        toolName: 'search_knowledge_base',
        arguments: { query: 'carry over' },
      }),
      ev('text-delta', { agent: 'researcher', parentCallId: 'd1', text: 'Found the carry-over rule.' }),
    ]);
    renderWithProviders(<ToolStep tool={tool!} />);

    const [outer, inner] = screen.getAllByTestId('tool-card');
    expect(within(outer!).getByText('Researcher working')).toBeInTheDocument();
    expect(within(outer!).getByText('“Check carry-over rules”')).toBeInTheDocument();
    const activity = within(outer!).getByTestId('subagent-activity');
    expect(within(activity).queryByText('claude-opus-5-5 · anthropic')).toBeNull();
    expect(inner).toBeDefined();
    expect(within(inner!).getByText('Searching the knowledge base')).toBeInTheDocument();
    expect(within(inner!).getByText('“carry over”')).toBeInTheDocument();
    expect(within(activity).getByText('Found the carry-over rule.')).toBeInTheDocument();
  });

  it('resolves source ids to titles with one batched request, falling back to the id', async () => {
    const fetchMock = mockFetch(
      sourcesRoutes(() => [
        sourceDto({ id: sourceGuid(1), title: 'Employee handbook' }),
        sourceDto({ id: sourceGuid(2), title: 'Leave policy', status: 'processing' }),
      ]),
    );
    const tools = toolsFrom([
      ev('tool-call', { callId: 'c1', toolName: 'read_source', arguments: { source_id: sourceGuid(1) } }),
      ev('tool-call', { callId: 'c2', toolName: 'search_knowledge_base', arguments: { query: 'leave', source_ids: [sourceGuid(2).toUpperCase()] } }),
      ev('tool-call', { callId: 'c3', toolName: 'read_source', arguments: { source_id: sourceGuid(3) } }),
      ev('tool-call', { callId: 'c4', toolName: 'read_source', arguments: { source_id: sourceGuid(1) } }),
    ]);
    renderWithProviders(
      <>
        {tools.map((tool) => (
          <ToolStep key={tool.callId} tool={tool} />
        ))}
      </>,
    );

    const cards = screen.getAllByTestId('tool-card');
    expect(await within(cards[0]!).findByText('Employee handbook')).toBeInTheDocument();
    expect(await within(cards[1]!).findByText('· Leave policy')).toBeInTheDocument();
    expect(within(cards[3]!).getByText('Employee handbook')).toBeInTheDocument();
    expect(within(cards[2]!).getByText(sourceGuid(3))).toBeInTheDocument();

    const requests = requestUrls(fetchMock, 'GET /api/sources');
    expect(requests).toHaveLength(1);
    expect(requests[0]!.searchParams.get('limit')).toBe('100');
    expect(requests[0]!.searchParams.get('ids')!.split(',').sort()).toEqual([sourceGuid(1), sourceGuid(2), sourceGuid(3)]);
  });

  it('uses titles known on the page and never sends ids that are not GUIDs', async () => {
    const fetchMock = mockFetch(sourcesRoutes(() => []));
    const tools = toolsFrom([
      ev('tool-call', { callId: 'c1', toolName: 'read_source', arguments: { source_id: 'src-handbook' } }),
      ev('tool-call', { callId: 'c2', toolName: 'read_source', arguments: { source_id: 'the handbook' } }),
    ]);
    renderWithProviders(
      <SourceTitleContext.Provider value={(id) => (id === 'src-handbook' ? 'Employee handbook' : undefined)}>
        {tools.map((tool) => (
          <ToolStep key={tool.callId} tool={tool} />
        ))}
      </SourceTitleContext.Provider>,
    );
    const [known, invented] = screen.getAllByTestId('tool-card');
    expect(within(known!).getByText('Employee handbook')).toBeInTheDocument();
    expect(within(invented!).getByText('the handbook')).toBeInTheDocument();
    await new Promise((resolve) => setTimeout(resolve, 20));
    await waitFor(() => expect(fetchCalls(fetchMock)).toEqual([]));
  });
});
