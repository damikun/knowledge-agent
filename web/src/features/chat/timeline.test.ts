import { describe, expect, it } from 'vitest';
import { messageDto, TURN_ID, turnDto } from '../../test/fixtures';
import { chatReducer, createChatState, turnFromDto, type TurnView } from './chatReducer';
import { buildTimeline, lastSequence, mergeMessages, ownTurnIds, pendingQuestion } from './timeline';

const NEXT = 'turn-2';

function ownTurn(message: string, sentAfterSequence: number, overrides: Partial<TurnView> = {}): TurnView {
  const state = chatReducer(createChatState(), { type: 'send', id: `local-${message}`, message, at: 0, sentAfterSequence });
  return { ...state.turns[0]!, ...overrides };
}

function followedTurn(turnId: string, message: string): TurnView {
  return chatReducer(createChatState(), { type: 'follow', turnId, message, at: 0 }).turns[0]!;
}

const firstTurn = turnFromDto(turnDto());
const storedMessages = [messageDto({ sequence: 1 }), messageDto({ sequence: 2, author: 'assistant', text: 'You get 25 days [S1].' })];

describe('timeline', () => {
  it('merges stored and live messages by sequence', () => {
    const live = [messageDto({ sequence: 3, text: 'Next?' }), messageDto({ sequence: 2, author: 'assistant', text: 'newer copy' })];
    const merged = mergeMessages(storedMessages, live);
    expect(merged.map((m) => [m.sequence, m.text])).toEqual([
      [1, 'How many vacation days do I get?'],
      [2, 'newer copy'],
      [3, 'Next?'],
    ]);
    expect(lastSequence(merged)).toBe(3);
    expect(lastSequence([])).toBe(0);
  });

  it('finds the server turn of a turn that ended before done: the first matching question stored after it was sent', () => {
    const messages = [
      ...storedMessages,
      messageDto({ sequence: 3, text: 'Same question', turnId: 'other-tab' }),
      messageDto({ sequence: 5, text: 'Same question', turnId: NEXT }),
    ];
    const stopped = ownTurn('Same question', 4, { status: 'cancelled' });
    const done = { ...ownTurn('Done one', 2), id: 'turn-done', persisted: true, status: 'complete' as const };
    const neverStored = ownTurn('Refused', 2, { status: 'error' });
    expect(ownTurnIds([done, stopped, neverStored], messages)).toEqual(
      new Map([
        [done.key, 'turn-done'],
        [stopped.key, NEXT],
      ]),
    );
  });

  it('takes the latest question while nothing shows its answer', () => {
    const messages = [...storedMessages, messageDto({ sequence: 3, text: 'Next?', turnId: NEXT })];
    expect(pendingQuestion(messages, new Set([TURN_ID]))?.turnId).toBe(NEXT);
    expect(pendingQuestion(messages, new Set([TURN_ID, NEXT]))).toBeNull();
    expect(pendingQuestion([messageDto({ sequence: 1, turnId: 'lost' }), ...messages.slice(1)], new Set([TURN_ID, NEXT]))).toBeNull();
  });

  it('shows the latest question on its own until its answer arrives', () => {
    const messages = [...storedMessages, messageDto({ sequence: 3, text: 'Next?', turnId: NEXT })];
    const turns = buildTimeline({ history: [firstTurn], messages, own: [], followed: [] });
    expect(turns.map((t) => [t.key, t.status, t.userMessage])).toEqual([
      [TURN_ID, 'complete', 'How many vacation days do I get?'],
      [NEXT, 'pending', 'Next?'],
    ]);

    const followed = followedTurn(NEXT, 'Next?');
    expect(buildTimeline({ history: [firstTurn], messages, own: [], followed: [followed] }).at(-1)).toBe(followed);
  });

  it('shows a followed turn only until it is stored', () => {
    const messages = [...storedMessages, messageDto({ sequence: 3, text: 'Next?', turnId: NEXT })];
    const followed = followedTurn(NEXT, 'Next?');
    const stored = turnFromDto(turnDto({ id: NEXT, sequence: 2, userMessage: 'Next?' }));
    const turns = buildTimeline({ history: [firstTurn, stored], messages, own: [], followed: [followed] });
    expect(turns).toEqual([firstTurn, stored]);
  });

  it('keeps the live copy of a finished own turn and swaps a stopped one for the stored copy', () => {
    const messages = [...storedMessages, messageDto({ sequence: 3, text: 'Next?', turnId: NEXT })];
    const live = { ...firstTurn, key: 'local-1', reasoning: 'Thinking…' };
    const stopped = ownTurn('Next?', 2, { status: 'cancelled', text: 'Part' });

    const whileRunning = buildTimeline({ history: [firstTurn], messages, own: [live, stopped], followed: [followedTurn(NEXT, 'Next?')] });
    expect(whileRunning).toEqual([live, stopped]);

    const stored = turnFromDto(turnDto({ id: NEXT, sequence: 2, userMessage: 'Next?', assistantMessage: 'The whole answer.' }));
    expect(buildTimeline({ history: [firstTurn, stored], messages, own: [live, stopped], followed: [] })).toEqual([live, stored]);
  });

  it('keeps an own turn the server never stored, such as a refused one', () => {
    const failed = ownTurn('Hello', 2, { status: 'error' });
    expect(buildTimeline({ history: [firstTurn], messages: storedMessages, own: [failed], followed: [] })).toEqual([firstTurn, failed]);
  });
});
