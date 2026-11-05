import { useQueryClient } from '@tanstack/react-query';
import { useCallback, useEffect, useRef, useState } from 'react';
import { errorMessage } from '../../lib/api';
import { createClientId } from '../../lib/ids';
import { isAbortError } from '../../lib/sse';
import { chatReducer, createChatState, type TurnView } from '../chat/chatReducer';
import { helpKeys, streamHelp } from './api';
import { clearThread, loadThread, newSessionId, saveThread } from './storage';
import type { HelpHistoryMessage, HelpTurn } from './types';

const HISTORY_TURNS = 6;
const MODEL_NOT_CONFIGURED = 'model_not_configured';

export interface HelpChat {
  turns: HelpTurn[];
  /** The streaming answer, folded by the chat reducer. */
  live: TurnView | null;
  busy: boolean;
  ask: (question: string) => void;
  stop: () => void;
  reset: () => void;
}

function history(turns: readonly HelpTurn[]): HelpHistoryMessage[] {
  return turns.slice(-HISTORY_TURNS).flatMap((turn) => [
    { role: 'user' as const, text: turn.question },
    ...(turn.text ? [{ role: 'assistant' as const, text: turn.text }] : []),
  ]);
}

/** On a no-model error the help status is refetched, so the panel shows it as disabled. */
export function useHelpChat(page: string): HelpChat {
  const queryClient = useQueryClient();
  const [thread, setThread] = useState(loadThread);
  const [live, setLive] = useState<TurnView | null>(null);
  const [busy, setBusy] = useState(false);
  // Lets a new question, Stop or New chat abort the running request.
  const controllerRef = useRef<AbortController | null>(null);

  useEffect(() => saveThread(thread), [thread]);
  useEffect(() => () => controllerRef.current?.abort(), []);

  const push = useCallback((turn: HelpTurn) => setThread((current) => ({ ...current, turns: [...current.turns, turn] })), []);

  const ask = useCallback(
    (question: string) => {
      controllerRef.current?.abort();
      const controller = new AbortController();
      controllerRef.current = controller;
      const id = createClientId();
      const current = thread;
      const sessionId = current.sessionId ?? newSessionId();
      if (sessionId !== current.sessionId) {
        setThread((value) => ({ ...value, sessionId }));
      }
      setBusy(true);

      const run = async () => {
        let state = chatReducer(createChatState(), { type: 'send', id, message: question, at: Date.now() });
        setLive(state.turns[0] ?? null);
        try {
          const events = streamHelp(
            { message: question, page, history: history(current.turns), ...(sessionId ? { sessionId } : {}) },
            controller.signal,
          );
          for await (const event of events) {
            state = chatReducer(state, { type: 'event', event, at: Date.now() });
            if (controllerRef.current === controller) {
              setLive(state.turns[0] ?? null);
            }
          }
        } catch (error) {
          state = chatReducer(state, isAbortError(error) ? { type: 'aborted', at: Date.now() } : { type: 'failed', message: errorMessage(error), at: Date.now() });
        }
        const turn = state.turns[0];
        // Superseded by a newer request.
        if (controllerRef.current !== controller) {
          return;
        }
        setLive(null);
        if (!turn) {
          return;
        }
        if (turn.errors.some((error) => error.code === MODEL_NOT_CONFIGURED)) {
          void queryClient.invalidateQueries({ queryKey: helpKeys.status });
          return;
        }
        const error = turn.errors.find((item) => item.code !== 'cancelled');
        push({ id, question, text: turn.text, citations: turn.citations, error: error?.message ?? null });
      };

      void run().finally(() => {
        if (controllerRef.current === controller) {
          controllerRef.current = null;
          setBusy(false);
        }
      });
    },
    [page, push, queryClient, thread],
  );

  const stop = useCallback(() => controllerRef.current?.abort(), []);

  const reset = useCallback(() => {
    controllerRef.current?.abort();
    controllerRef.current = null;
    setBusy(false);
    setLive(null);
    clearThread();
    setThread({ sessionId: null, turns: [] });
  }, []);

  return { turns: thread.turns, live, busy, ask, stop, reset };
}
