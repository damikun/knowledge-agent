import { useQueryClient } from '@tanstack/react-query';
import { useEffect, useMemo, useReducer, useRef } from 'react';
import { conversationKeys } from './api';
import { chatReducer, createChatState, type ChatState, type TurnView } from './chatReducer';
import { isQuestion, knownTurnIds, lastSequence, mergeMessages, pendingQuestion } from './timeline';
import type { ChatEvent, ConversationDto, ConversationLiveEvent, ConversationMessageDto } from './types';
import { useConversationEvents, type ConversationEventOptions } from './useConversationEvents';

export interface LiveConversationOptions extends ConversationEventOptions {
  conversationId: string | null;
  /** Off while this tab's own POST /api/chat stream renders the turn, so it isn't shown twice. */
  enabled: boolean;
  conversation: ConversationDto | undefined;
  history: readonly TurnView[];
  ownTurns: readonly TurnView[];
}

export interface LiveConversation {
  /** Turns written elsewhere (another tab, or before a reload). */
  turns: TurnView[];
  messages: ConversationMessageDto[];
  lastSequence: number;
}

const NO_MESSAGES: ConversationMessageDto[] = [];

interface LiveState {
  conversationId: string | null;
  /** Bumped per conversation switch so sequence state from an earlier visit is ignored. */
  session: number;
  followed: ChatState;
  received: ConversationMessageDto[];
  replay: boolean;
}

type LiveAction =
  | { type: 'switch'; conversationId: string | null }
  | { type: 'open' }
  | { type: 'message'; message: ConversationMessageDto; at: number }
  | {
      type: 'turn';
      event: ChatEvent;
      at: number;
      conversationId: string | null;
      stored: readonly ConversationMessageDto[];
      history: readonly TurnView[];
      ownTurns: readonly TurnView[];
    };

function initialState(conversationId: string | null, session = 0): LiveState {
  return { conversationId, session, followed: createChatState(conversationId), received: NO_MESSAGES, replay: false };
}

// Each event depends on the state the previous ones left, so the reducer owns the followed turn, not render.
function liveReducer(state: LiveState, action: LiveAction): LiveState {
  switch (action.type) {
    case 'switch':
      return initialState(action.conversationId, state.session + 1);
    case 'open':
      return state.replay ? state : { ...state, replay: true };
    case 'message': {
      // Only one turn runs at a time, so a new question ends the one still streaming.
      const last = state.followed.turns.at(-1);
      const ended = isQuestion(action.message) && last?.status === 'streaming' && last.id !== action.message.turnId;
      return {
        ...state,
        followed: ended ? chatReducer(state.followed, { type: 'aborted', at: action.at }) : state.followed,
        received: [...state.received, action.message],
      };
    }
    case 'turn': {
      let followed = state.followed;
      if (state.replay) {
        followed = chatReducer(followed, { type: 'reset', conversationId: action.conversationId });
      }
      const unchanged = { ...state, followed, replay: false };
      if (followed.turns.at(-1)?.status !== 'streaming') {
        // A turn's first event arrives before its question is stored.
        if (action.event.event === 'conversation') {
          return state.replay ? unchanged : state;
        }
        const messages = mergeMessages(action.stored, state.received);
        const known = knownTurnIds({ history: action.history, messages, own: action.ownTurns, followed: followed.turns });
        const question = pendingQuestion(messages, known);
        if (!question?.turnId) {
          // Already shown, e.g. a turn this tab stopped.
          return state.replay ? unchanged : state;
        }
        followed = chatReducer(followed, { type: 'follow', turnId: question.turnId, message: question.text, at: action.at });
      }
      return { ...state, followed: chatReducer(followed, { type: 'event', event: action.event, at: action.at }), replay: false };
    }
  }
}

/** Follows the open conversation and feeds turns started elsewhere through the chat reducer. */
export function useLiveConversation({
  conversationId,
  enabled,
  conversation,
  history,
  ownTurns,
  retryDelaysMs,
}: LiveConversationOptions): LiveConversation {
  const queryClient = useQueryClient();
  const [state, dispatch] = useReducer(liveReducer, conversationId, (id) => initialState(id));
  // Reset before the previous conversation's turns get painted.
  if (state.conversationId !== conversationId) {
    dispatch({ type: 'switch', conversationId });
  }
  // Last sequence seen, for resuming after a reconnect.
  const lastSeenRef = useRef({ session: state.session, sequence: 0 });
  const refetchTimerRef = useRef<number | null>(null);

  const stored = conversation?.messages ?? NO_MESSAGES;
  const messages = useMemo(() => mergeMessages(stored, state.received), [stored, state.received]);

  const lastSeen = () => (lastSeenRef.current.session === state.session ? lastSeenRef.current.sequence : 0);
  const markSeen = (sequence: number) => {
    lastSeenRef.current = { session: state.session, sequence };
  };

  // Debounced: a reconnect replays a burst of messages.
  const refetch = () => {
    if (refetchTimerRef.current !== null) {
      return;
    }
    refetchTimerRef.current = window.setTimeout(() => {
      refetchTimerRef.current = null;
      void queryClient.invalidateQueries({ queryKey: conversationKeys.all });
    }, 0);
  };

  useEffect(
    () => () => {
      if (refetchTimerRef.current !== null) {
        window.clearTimeout(refetchTimerRef.current);
      }
    },
    [],
  );

  const onMessage = (message: ConversationMessageDto) => {
    if (message.sequence <= lastSeen()) {
      return;
    }
    markSeen(message.sequence);
    dispatch({ type: 'message', message, at: Date.now() });
    refetch();
  };

  const onTurnEvent = (event: ChatEvent) => {
    dispatch({ type: 'turn', event, at: Date.now(), conversationId, stored, history, ownTurns });
    if (event.event === 'done') {
      refetch();
    }
  };

  useConversationEvents(
    conversationId,
    enabled,
    {
      afterSequence: () => {
        markSeen(Math.max(lastSeen(), lastSequence(stored)));
        return lastSeen();
      },
      onOpen: () => dispatch({ type: 'open' }),
      onEvent: (event: ConversationLiveEvent) => {
        if (event.type === 'message') {
          onMessage(event.message);
        } else if (event.type === 'turn') {
          onTurnEvent(event.event);
        } else {
          refetch();
        }
      },
    },
    { retryDelaysMs },
  );

  return { turns: state.followed.turns, messages, lastSequence: lastSequence(messages) };
}
