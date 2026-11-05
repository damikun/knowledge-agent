import { createContext, useContext } from 'react';

export interface ChatDisplay {
  /** Admins see the developer details of answers: model, prompt, tokens, timings, scores and trace links. */
  developer: boolean;
  traceUrl?: (traceId: string) => string | null;
  prompt?: string | null;
}

export const ChatDisplayContext = createContext<ChatDisplay>({ developer: false });

export function useChatDisplay(): ChatDisplay {
  return useContext(ChatDisplayContext);
}
