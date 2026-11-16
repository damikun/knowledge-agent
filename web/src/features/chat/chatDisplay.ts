import { createContext, useContext } from 'react';

export interface ChatDisplay {
  developer: boolean;
  traceUrl?: (traceId: string) => string | null;
  prompt?: string | null;
}

export const ChatDisplayContext = createContext<ChatDisplay>({ developer: false });

export function useChatDisplay(): ChatDisplay {
  return useContext(ChatDisplayContext);
}
