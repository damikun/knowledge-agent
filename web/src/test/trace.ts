import { chatReducer, createChatState, type TurnView } from '../features/chat/chatReducer';
import { fullTurnEvents } from './fixtures';

/** The full fixture turn: a search, the researcher with one search of its own, and an answer citing S1 to S3. */
export function tracedTurn(): TurnView {
  let state = chatReducer(createChatState(), { type: 'send', id: 'client-1', message: 'How many vacation days do I get?', at: 0 });
  for (const event of fullTurnEvents()) {
    state = chatReducer(state, { type: 'event', event, at: 1000 });
  }
  return state.turns[0]!;
}
