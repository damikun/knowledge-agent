import { createTurn, type TurnView } from '../chat/chatReducer';
import { isQuestion } from '../chat/timeline';
import type { ConversationMessageDto } from '../chat/types';

/** A request for a person that nobody joined in time. */
export const MISSED_REQUEST = 'Missed request';

export type InboxItem = { kind: 'message'; message: ConversationMessageDto; turn: TurnView | null } | { kind: 'turn'; turn: TurnView };

/** The stored turn adds the tool calls, the model and the tokens. */
function answerTurn(message: ConversationMessageDto, stored: TurnView | null): TurnView {
  const base = stored ?? { ...createTurn(message.id, '', null), persisted: true, status: 'complete' as const };
  return { ...base, key: message.id, text: message.text, citations: base.citations.length > 0 ? base.citations : message.citations };
}

/** Kept separate so answers keep their identity while one streams. */
export function answerTurns(messages: readonly ConversationMessageDto[], history: readonly TurnView[]): Map<string, TurnView> {
  const storedById = new Map(history.map((turn) => [turn.id, turn] as const));
  return new Map(
    messages
      .filter((message) => message.author === 'assistant')
      .map((message) => [message.id, answerTurn(message, message.turnId ? (storedById.get(message.turnId) ?? null) : null)] as const),
  );
}

// A streaming turn shows at the end until its answer is stored; one that ended without an answer, after its question.
export function inboxTimeline(
  messages: readonly ConversationMessageDto[],
  history: readonly TurnView[],
  followed: readonly TurnView[],
  answers: ReadonlyMap<string, TurnView> = answerTurns(messages, history),
): InboxItem[] {
  const answered = new Set(messages.flatMap((message) => (message.author === 'assistant' && message.turnId ? [message.turnId] : [])));
  const storedById = new Map(history.map((turn) => [turn.id, turn] as const));
  const followedIds = new Set(followed.map((turn) => turn.id));
  const items: InboxItem[] = [];
  for (const message of messages) {
    if (message.author === 'assistant') {
      items.push({ kind: 'message', message, turn: answers.get(message.id) ?? answerTurn(message, null) });
      continue;
    }
    items.push({ kind: 'message', message, turn: null });
    const stored = message.turnId ? storedById.get(message.turnId) : undefined;
    if (isQuestion(message) && stored && !answered.has(stored.id) && !followedIds.has(stored.id) && stored.status !== 'complete') {
      items.push({ kind: 'turn', turn: stored });
    }
  }
  for (const turn of followed) {
    if (!answered.has(turn.id)) {
      items.push({ kind: 'turn', turn });
    }
  }
  return items;
}
