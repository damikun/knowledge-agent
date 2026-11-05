import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { citationS1, citationS2, citationS3, turnDto } from '../../test/fixtures';
import { renderWithProviders } from '../../test/utils';
import { numberSources } from './answerSources';
import { AssistantMessage } from './AssistantMessage';
import { ChatDisplayContext } from './chatDisplay';
import { citationIndex, createTurn, turnFromDto } from './chatReducer';
import { createCitationLookup } from './citationContext';
import type { CitationDto } from './types';

function citation(n: number, overrides: Partial<CitationDto> = {}): CitationDto {
  return {
    ref: `S${n}`,
    sourceId: `src-${n}`,
    chunkId: `chunk-${n}`,
    sourceTitle: `Source number ${n}`,
    sourceUrl: `https://docs.example.com/page-${n}`,
    page: null,
    snippet: `Passage ${n}`,
    score: 0.5,
    ...overrides,
  };
}

describe('numberSources', () => {
  it('gives each source one number in order of first citation, also for several passages', () => {
    const lookup = createCitationLookup([citationS1, citationS2, citationS3]);
    const sources = numberSources(['S2', 'S1', 'S3'], lookup);
    expect(sources.cited.map((source) => [source.number, source.title, source.citations.length])).toEqual([
      [1, 'Leave policy', 1],
      [2, 'Employee handbook', 2],
    ]);
    expect(sources.byRef('s3')?.number).toBe(2);
    expect(sources.byRef('S9')).toBeUndefined();
  });
});

describe('AssistantMessage', () => {
  it('renders markdown and turns [S#] tags into numbered markers that share a number per source', () => {
    const turn = turnFromDto(
      turnDto({
        assistantMessage: 'You get **25 days** [S1]. Requests go to your manager [S2]. Days carry over [S3].\n\n`[S1]` in code stays literal.',
        citations: [citationS1, citationS2, citationS3],
      }),
    );
    renderWithProviders(<AssistantMessage turn={turn} />);

    const article = screen.getByRole('article', { name: 'Assistant answer' });
    expect(within(article).getByText('25 days').tagName).toBe('STRONG');
    const markers = within(article).getAllByRole('button', { name: /^Source \d/ });
    expect(markers.map((marker) => marker.textContent)).toEqual(['1', '2', '1']);
    expect(markers[0]).toHaveAccessibleName('Source 1: Employee handbook, page 4');
    expect(markers[2]).toHaveAccessibleName('Source 1: Employee handbook, page 9');
    expect(within(article).getByText('[S1]').tagName).toBe('CODE');
    expect(within(article).queryByText(/claude-opus-5-5/)).toBeNull();
  });

  it('opens a card with the source on focus and closes it with Esc', async () => {
    const user = userEvent.setup();
    const turn = turnFromDto(turnDto({ assistantMessage: 'Twenty-five days [S1].', citations: [citationS1] }));
    renderWithProviders(<AssistantMessage turn={turn} />);

    await user.tab();
    const marker = screen.getByRole('button', { name: /Source 1/ });
    expect(marker).toHaveFocus();
    const card = await screen.findByRole('dialog', { name: 'Source 1' });
    expect(within(card).getByText('Employee handbook')).toBeInTheDocument();
    expect(within(card).getByText('File · Page 4')).toBeInTheDocument();
    expect(within(card).getByText(citationS1.snippet)).toBeInTheDocument();
    expect(within(card).getByRole('link', { name: /open source/i })).toHaveAttribute('href', '/sources?source=src-handbook');

    await user.keyboard('{Escape}');
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Source 1' })).toBeNull());
    expect(marker).toHaveFocus();
  });

  it('opens the card on hover and links web sources to their page', async () => {
    const user = userEvent.setup();
    const turn = turnFromDto(turnDto({ assistantMessage: 'Approval by the manager [S2].', citations: [citationS2] }));
    renderWithProviders(<AssistantMessage turn={turn} />);
    await user.hover(screen.getByRole('button', { name: /Source 1/ }));
    const card = await screen.findByRole('dialog', { name: 'Source 1' });
    expect(within(card).getByText('intranet.example.com')).toBeInTheDocument();
    expect(within(card).getByRole('link', { name: /open source/i })).toHaveAttribute('href', 'https://intranet.example.com/leave-policy');
  });

  it('resolves citations from earlier turns and marks unknown tags', () => {
    const earlier = turnFromDto(turnDto({ id: 'earlier', citations: [citationS3] }));
    const turn = turnFromDto(turnDto({ assistantMessage: 'As mentioned before [S3]. Unknown [S99].', citations: [] }));
    renderWithProviders(<AssistantMessage turn={turn} conversationCitations={citationIndex([earlier])} />);
    expect(screen.getByRole('button', { name: 'Source 1: Employee handbook, page 9' })).toBeInTheDocument();
    expect(screen.getByText('Unknown citation S99').parentElement).toHaveAttribute('title', 'This citation does not match a retrieved passage');
  });

  it('lists each cited source once, with its pages and passages', () => {
    const turn = turnFromDto(turnDto({ assistantMessage: 'A [S1]. B [S2]. C [S3].', citations: [citationS1, citationS2, citationS3] }));
    renderWithProviders(<AssistantMessage turn={turn} />);
    const list = screen.getByRole('list', { name: 'Sources' });
    const items = within(list).getAllByRole('listitem');
    expect(items).toHaveLength(2);
    expect(items[0]).toHaveTextContent('1Employee handbookFile · Pages 4, 9 · 2 passages');
    expect(within(items[0]!).getByRole('link')).toHaveAttribute('href', '/sources?source=src-handbook');
    expect(items[1]).toHaveTextContent('2Leave policyintranet.example.com');
    expect(within(items[1]!).getByRole('link')).toHaveAttribute('href', 'https://intranet.example.com/leave-policy');
  });

  it('shows the first three sources and "+N more" for the rest', async () => {
    const user = userEvent.setup();
    const citations = [1, 2, 3, 4, 5].map((n) => citation(n, n === 1 ? { sourceTitle: 'A very long source title '.repeat(8) } : {}));
    const turn = turnFromDto(turnDto({ assistantMessage: 'One [S1][S2] two [S3] three [S4, S5].', citations }));
    renderWithProviders(<AssistantMessage turn={turn} />);
    const list = screen.getByRole('list', { name: 'Sources' });
    expect(within(list).getAllByRole('link')).toHaveLength(3);
    expect(within(list).getAllByRole('link')[0]).toHaveAttribute('title', expect.stringContaining('A very long source title'));
    await user.click(within(list).getByRole('button', { name: '+2 more' }));
    expect(within(list).getAllByRole('link')).toHaveLength(5);
    expect(within(list).queryByRole('button', { name: /more/ })).toBeNull();
  });

  it('keeps developer details behind a Details button that only admins get', async () => {
    const user = userEvent.setup();
    const turn = turnFromDto(turnDto({ traceId: 'trace-1' }));
    const { unmount } = renderWithProviders(<AssistantMessage turn={turn} />);
    expect(screen.getByRole('button', { name: 'Copy answer' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Details' })).toBeNull();
    unmount();

    renderWithProviders(
      <ChatDisplayContext.Provider value={{ developer: true, prompt: 'orchestrator-system v3 (langfuse)', traceUrl: (id) => `https://langfuse.example.com/trace/${id}` }}>
        <AssistantMessage turn={turn} />
      </ChatDisplayContext.Provider>,
    );
    await user.click(screen.getByRole('button', { name: 'Details' }));
    const details = await screen.findByRole('dialog', { name: 'Answer details' });
    expect(details).toHaveTextContent('claude-opus-5-5 · anthropic');
    expect(details).toHaveTextContent('orchestrator-system v3 (langfuse)');
    expect(details).toHaveTextContent('1,200 in · 80 out');
    expect(details).toHaveTextContent('2.4 s');
    expect(within(details).getByRole('link', { name: /Open in Langfuse/ })).toHaveAttribute('href', 'https://langfuse.example.com/trace/trace-1');
  });

  it('shows inline errors and a typing indicator while waiting', () => {
    const { rerender } = renderWithProviders(<AssistantMessage turn={createTurn('l1', 'Hi', 0)} />);
    expect(screen.getByRole('status')).toHaveTextContent('Waiting for the answer…');
    expect(screen.getByRole('article')).toHaveAttribute('aria-busy', 'true');

    rerender(
      <AssistantMessage
        turn={{ ...createTurn('l1', 'Hi', 0), status: 'error', errors: [{ message: 'No chat model is configured.', code: 'model_not_configured', agent: 'orchestrator' }] }}
      />,
    );
    const alert = screen.getByRole('alert');
    expect(alert).toHaveTextContent('No chat model is configured.');
    expect(within(alert).getByRole('link', { name: 'Open AI models' })).toHaveAttribute('href', '/models');
  });

  it('shows a stopped answer with its text and a quiet note instead of an error', () => {
    renderWithProviders(<AssistantMessage turn={{ ...createTurn('t1', 'Hi', 0), status: 'cancelled', text: 'New employees get', finishReason: 'stopped' }} />);
    const answer = screen.getByRole('article');
    expect(answer).toHaveTextContent('New employees get');
    expect(within(answer).getByText('Stopped')).toBeInTheDocument();
    expect(within(answer).queryByRole('alert')).toBeNull();
    expect(answer).toHaveAttribute('aria-busy', 'false');
  });
});
