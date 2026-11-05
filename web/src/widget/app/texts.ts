// Every string the frame shows. A widget's `texts` replace these by key, so admins can translate or reword them.
export const DEFAULT_TEXTS = {
  launcherLabel: 'Chat with us',
  close: 'Close chat',
  send: 'Send',
  newChat: 'New chat',
  endChat: 'End chat',
  talkToPerson: 'Talk to a person',
  waitingForPerson: 'Waiting for someone from the team to join.',
  keepChatting: 'Keep chatting with the assistant',
  personJoined: 'You are chatting with {name}.',
  feedbackPrompt: 'Was this helpful?',
  feedbackThanks: 'Thanks for your feedback.',
  feedbackComment: 'What could be better?',
  ratingPrompt: 'How was this chat?',
  contactTitle: 'How can we reach you?',
  contactName: 'Name',
  contactEmail: 'Email',
  contactSubmit: 'Continue',
  sources: 'Sources',
  offline: 'Connection lost. Trying to reconnect.',
  rateLimited: 'You are sending messages too quickly. Wait a moment and try again.',
  dailyCap: 'This assistant has answered all it can for today. Please come back tomorrow.',
  error: 'Something went wrong. Please try again.',
  poweredBy: 'Powered by Knowledge Agent',
  // The API has no text keys for these yet, so they show in English unless it gains them.
  message: 'Message',
  messages: 'Messages',
  placeholder: 'Ask a question',
  menu: 'More options',
  thinking: 'Writing an answer',
  searching: 'Searching the knowledge base',
  helpful: 'Helpful',
  notHelpful: 'Not helpful',
  skip: 'Skip',
  cancel: 'Cancel',
  dismiss: 'Dismiss',
  ended: 'This chat has ended.',
  busy: 'The previous answer is still being written. Try again in a moment.',
  charactersLeft: '{count} characters left',
  you: 'You',
  team: 'our team',
  rating: '{count} out of 5',
  unavailable: 'This chat is not available.',
  retry: 'Try again',
  loading: 'Loading',
  newTab: 'opens in a new tab',
  source: 'Source {count}',
  confirmAction: 'Confirm',
  cancelAction: 'Cancel',
} as const;

type TextKey = keyof typeof DEFAULT_TEXTS;
export type Texts = (key: TextKey, values?: Record<string, string | number>) => string;

export function createTexts(overrides?: Readonly<Record<string, string>> | null): Texts {
  return (key, values) => {
    const custom = overrides?.[key];
    let result: string = typeof custom === 'string' && custom.trim() !== '' ? custom : DEFAULT_TEXTS[key];
    if (values) {
      for (const [name, value] of Object.entries(values)) {
        result = result.replaceAll(`{${name}}`, String(value));
      }
    }
    return result;
  };
}
