import { createTurn, mergeTurns, type TurnView } from './chatReducer';
import type { ConversationMessageDto } from './types';

export function isQuestion(message: ConversationMessageDto): boolean {
  return message.author === 'user' || message.author === 'visitor';
}

/** Stored and live messages by sequence; the later copy of a sequence wins. */
export function mergeMessages(stored: readonly ConversationMessageDto[], live: readonly ConversationMessageDto[]): ConversationMessageDto[] {
  if (live.length === 0) {
    return stored.slice().sort((a, b) => a.sequence - b.sequence);
  }
  const bySequence = new Map<number, ConversationMessageDto>();
  for (const message of [...stored, ...live]) {
    bySequence.set(message.sequence, message);
  }
  return [...bySequence.values()].sort((a, b) => a.sequence - b.sequence);
}

export function lastSequence(messages: readonly ConversationMessageDto[]): number {
  return messages.reduce((max, message) => Math.max(max, message.sequence), 0);
}

/**
 * Server turn ids by turn key. A turn that ended early here still runs on the server: it is the turn
 * of the first matching question stored after the sequence it was sent at.
 */
export function ownTurnIds(own: readonly TurnView[], messages: readonly ConversationMessageDto[]): Map<string, string> {
  const ids = new Map<string, string>();
  const taken = new Set<string>();
  for (const turn of own) {
    if (turn.persisted) {
      ids.set(turn.key, turn.id);
      taken.add(turn.id);
    }
  }
  const questions = messages.filter((message) => isQuestion(message) && message.turnId);
  for (const turn of own) {
    const after = turn.sentAfterSequence;
    if (turn.persisted || after === null) {
      continue;
    }
    const match = questions.find(
      (question) => question.sequence > after && !taken.has(question.turnId!) && question.text.trim() === turn.userMessage.trim(),
    );
    if (match) {
      ids.set(turn.key, match.turnId!);
      taken.add(match.turnId!);
    }
  }
  return ids;
}

/** The latest question while no turn shows its answer: the one the turn being written (if any) answers. */
export function pendingQuestion(messages: readonly ConversationMessageDto[], knownTurnIds: ReadonlySet<string>): ConversationMessageDto | null {
  let latest: ConversationMessageDto | null = null;
  for (const message of messages) {
    if (isQuestion(message) && (latest === null || message.sequence > latest.sequence)) {
      latest = message;
    }
  }
  return latest?.turnId && !knownTurnIds.has(latest.turnId) ? latest : null;
}

export interface TimelineInput {
  history: readonly TurnView[];
  messages: readonly ConversationMessageDto[];
  own: readonly TurnView[];
  followed: readonly TurnView[];
}

export function knownTurnIds({ history, messages, own, followed }: TimelineInput): Set<string> {
  return new Set([...history.map((turn) => turn.id), ...ownTurnIds(own, messages).values(), ...followed.map((turn) => turn.id)]);
}

/**
 * The turns to show. A stored turn gives way to this tab's live copy, which also has the reasoning; a turn this tab
 * stopped gives way to the stored copy, which has the whole answer. A followed turn shows only until it is stored. The
 * latest question shows on its own while its answer has not reached this view.
 */
export function buildTimeline(input: TimelineInput): TurnView[] {
  const { history, messages, own, followed } = input;
  const ownIds = ownTurnIds(own, messages);
  const historyIds = new Set(history.map((turn) => turn.id));
  const shownOwn = own.filter((turn) => {
    const serverId = ownIds.get(turn.key);
    return turn.persisted || serverId === undefined || !historyIds.has(serverId);
  });
  const turns = mergeTurns(history, shownOwn);

  const shown = new Set([...historyIds, ...ownIds.values()]);
  for (const turn of followed) {
    if (!shown.has(turn.id)) {
      turns.push(turn);
      shown.add(turn.id);
    }
  }
  const question = pendingQuestion(messages, shown);
  if (question?.turnId) {
    turns.push({ ...createTurn(question.turnId, question.text, null), status: 'pending' });
  }
  return turns;
}

export type ThreadItem = { kind: 'turn'; turn: TurnView } | { kind: 'system'; message: ConversationMessageDto };

export function withSystemNotices(turns: readonly TurnView[], messages: readonly ConversationMessageDto[]): ThreadItem[] {
  const notices = messages.filter((message) => message.author === 'system' && message.text.trim()).sort((a, b) => a.sequence - b.sequence);
  if (notices.length === 0) {
    return turns.map((turn) => ({ kind: 'turn', turn }));
  }
  const ownIds = ownTurnIds(turns, messages);
  const questionSequence = new Map<string, number>();
  for (const message of messages) {
    if (message.turnId && isQuestion(message) && !questionSequence.has(message.turnId)) {
      questionSequence.set(message.turnId, message.sequence);
    }
  }
  const items: ThreadItem[] = [];
  let next = 0;
  for (const turn of turns) {
    const sequence = questionSequence.get(ownIds.get(turn.key) ?? turn.id);
    while (sequence !== undefined && next < notices.length && notices[next]!.sequence < sequence) {
      items.push({ kind: 'system', message: notices[next++]! });
    }
    items.push({ kind: 'turn', turn });
  }
  while (next < notices.length) {
    items.push({ kind: 'system', message: notices[next++]! });
  }
  return items;
}
