import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { supportUser } from '../../test/apiFixtures';
import { WIDGET_CONVERSATION_ID } from '../../test/inboxApi';
import { jsonResponse, mockFetch, problemResponse, renderWithProviders, requestUrls } from '../../test/utils';
import { SuggestReply } from './SuggestReply';
import { citedSources, toReplySuggestion } from './suggestReplyApi';

afterEach(() => {
  vi.unstubAllGlobals();
});

const SUGGEST = `POST /api/conversations/${WIDGET_CONVERSATION_ID}/suggest-reply`;

const citation = (ref: string, sourceId: string, sourceTitle: string) => ({
  ref,
  sourceId,
  chunkId: `${sourceId}-chunk`,
  sourceTitle,
  sourceUrl: null,
  page: null,
  snippet: 'Shipping to Norway costs 20 EUR.',
  score: 0.9,
});

function Harness({ initial = '' }: { initial?: string }) {
  const [text, setText] = useState(initial);
  return (
    <>
      <textarea aria-label="Reply" value={text} onChange={(event) => setText(event.target.value)} />
      <SuggestReply currentText={text} onDraft={setText} />
    </>
  );
}

function renderSuggest(initial?: string) {
  return renderWithProviders(<Harness initial={initial} />, { route: `/inbox/${WIDGET_CONVERSATION_ID}`, path: '/inbox/:conversationId?' });
}

describe('SuggestReply', () => {
  it('fills the reply box with an editable draft, shows its sources and restores the text it replaced', async () => {
    const user = userEvent.setup();
    const fetchMock = mockFetch({
      [SUGGEST]: () =>
        jsonResponse({
          text: 'Hi! Shipping to Norway costs 20 EUR [S1][S2].',
          citations: [citation('S1', 'source-1', 'Shipping rates'), citation('S2', 'source-1', 'Shipping rates')],
        }),
    });
    renderSuggest('Let me check');

    await user.click(screen.getByRole('button', { name: 'Suggest reply' }));

    const box = screen.getByRole('textbox', { name: 'Reply' });
    expect(await screen.findByDisplayValue('Hi! Shipping to Norway costs 20 EUR [S1][S2].')).toBe(box);
    expect(screen.getByRole('link', { name: 'Shipping rates' })).toHaveAttribute('href', '/sources?source=source-1');
    expect(requestUrls(fetchMock, `POST /api/conversations/${WIDGET_CONVERSATION_ID}/messages`)).toEqual([]);

    await user.click(screen.getByRole('button', { name: 'Restore my text' }));
    expect(box).toHaveValue('Let me check');
    expect(screen.queryByRole('button', { name: 'Restore my text' })).not.toBeInTheDocument();
  });

  it('links admins to Models when no chat model is connected', async () => {
    const user = userEvent.setup();
    mockFetch({ [SUGGEST]: () => problemResponse({ title: 'Model not configured', code: 'model_not_configured' }, 503) });
    renderSuggest();

    await user.click(screen.getByRole('button', { name: 'Suggest reply' }));

    expect(await screen.findByRole('link', { name: 'Connect one in Models' })).toHaveAttribute('href', '/models');
    expect(screen.getByRole('textbox', { name: 'Reply' })).toHaveValue('');
  });

  it('asks support staff to get an admin when no chat model is connected', async () => {
    const user = userEvent.setup();
    mockFetch({
      'GET /api/auth/me': supportUser,
      [SUGGEST]: () => problemResponse({ title: 'Model not configured', code: 'model_not_configured' }, 503),
    });
    renderSuggest();

    await user.click(screen.getByRole('button', { name: 'Suggest reply' }));

    expect(await screen.findByText(/Ask an admin to connect one/)).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Connect one in Models' })).not.toBeInTheDocument();
  });

  it('reads drafts defensively and lists each cited source once', () => {
    expect(toReplySuggestion({ text: 'Hi', citations: [{ ref: 'S1' }, citation('S2', 'b', '')] })).toEqual({
      text: 'Hi',
      citations: [citation('S2', 'b', 'Untitled source')],
    });
    expect(citedSources([citation('S1', 'a', 'A'), citation('S2', 'b', 'B'), citation('S3', 'a', 'A')])).toEqual([
      { sourceId: 'a', title: 'A', refs: ['S1', 'S3'] },
      { sourceId: 'b', title: 'B', refs: ['S2'] },
    ]);
  });
});
