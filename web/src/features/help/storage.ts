import type { HelpTurn } from './types';

// Session storage: the thread ends with the tab and is never stored server-side.
const KEY = 'ka-help-thread';
const MAX_TURNS = 30;

export interface HelpThread {
  sessionId: string | null;
  turns: HelpTurn[];
}

function isTurn(value: unknown): value is HelpTurn {
  const turn = value as Partial<HelpTurn> | null;
  return (
    typeof turn === 'object' &&
    turn !== null &&
    typeof turn.id === 'string' &&
    typeof turn.question === 'string' &&
    typeof turn.text === 'string' &&
    Array.isArray(turn.citations) &&
    (turn.text !== '' || typeof turn.error === 'string')
  );
}

export function loadThread(): HelpThread {
  try {
    const raw = window.sessionStorage.getItem(KEY);
    const parsed = raw ? (JSON.parse(raw) as Partial<HelpThread>) : null;
    return {
      sessionId: typeof parsed?.sessionId === 'string' ? parsed.sessionId : null,
      turns: Array.isArray(parsed?.turns) ? parsed.turns.filter(isTurn) : [],
    };
  } catch {
    return { sessionId: null, turns: [] };
  }
}

export function saveThread(thread: HelpThread): void {
  try {
    window.sessionStorage.setItem(KEY, JSON.stringify({ ...thread, turns: thread.turns.slice(-MAX_TURNS) }));
  } catch {
    // Storage may be full or blocked; the chat still works for this page view.
  }
}

export function clearThread(): void {
  try {
    window.sessionStorage.removeItem(KEY);
  } catch {}
}

/** Groups the traces of one help conversation; null where randomUUID is unavailable. */
export function newSessionId(): string | null {
  return typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function' ? crypto.randomUUID() : null;
}
