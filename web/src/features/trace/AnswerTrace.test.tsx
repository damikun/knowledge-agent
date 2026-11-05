import { fireEvent, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { tracedTurn } from '../../test/trace';
import { AnswerTrace } from './AnswerTrace';

// jsdom has no ResizeObserver, which React Flow needs.
class NoopResizeObserver {
  observe() {}
  unobserve() {}
  disconnect() {}
}

beforeEach(() => {
  vi.stubGlobal('ResizeObserver', NoopResizeObserver);
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('AnswerTrace', () => {
  // The graph is lazy-loaded, which can exceed the default wait when the whole suite runs.
  it('loads the graph, starts on the answer and shows the details of a step on click or Enter', { timeout: 15_000 }, async () => {
    const user = userEvent.setup();
    render(<AnswerTrace turn={tracedTurn()} />);
    const graph = await screen.findByRole('region', { name: /Answer trace/ }, { timeout: 8_000 });
    const details = await screen.findByRole('complementary', { name: 'Step details' });
    expect(await within(details).findByText('claude-opus-5-5')).toBeInTheDocument();

    // A plain click: d3-zoom's mousedown handler needs a window view that jsdom's pointer events lack.
    fireEvent.click(within(graph).getByRole('button', { name: /^Searched the knowledge base\. 412 ms/ }));
    expect(within(details).getByText('search_knowledge_base')).toBeInTheDocument();
    expect(within(details).getByText('Employee handbook')).toBeInTheDocument();
    expect(within(details).getByText('0.830')).toBeInTheDocument();

    within(graph).getByRole('button', { name: /^Researcher\. 3\.1 s/ }).focus();
    await user.keyboard('{Enter}');
    expect(within(details).getByText('Days carry over until March [S3].')).toBeInTheDocument();
  });
});
