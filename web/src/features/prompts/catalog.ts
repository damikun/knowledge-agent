import type { PromptSummaryDto } from './types';

export type PromptGroupId = 'answering' | 'search' | 'conversations' | 'evaluations' | 'other';

export interface PromptInfo {
  title: string;
  description: string;
  group: PromptGroupId;
}

const GROUPS: ReadonlyArray<{ id: PromptGroupId; label: string }> = [
  { id: 'answering', label: 'Answering' },
  { id: 'search', label: 'Search' },
  { id: 'conversations', label: 'Conversations' },
  { id: 'evaluations', label: 'Evaluations' },
  { id: 'other', label: 'Other prompts in Langfuse' },
];

// Must match PromptNames on the server.
const KNOWN: Readonly<Record<string, PromptInfo>> = {
  'orchestrator-system': {
    title: 'Answering agent',
    description: 'The main instructions agents answer with: how to search the sources, cite them and stay on topic.',
    group: 'answering',
  },
  'researcher-system': {
    title: 'Research helper',
    description: 'Instructions for the helper an agent sends to search the sources in depth on harder questions.',
    group: 'answering',
  },
  'reply-suggestion': {
    title: 'Reply suggestions',
    description: 'Drafts a reply for the team in the Inbox from the conversation and the sources, in the agent\'s language and tone.',
    group: 'answering',
  },
  rerank: {
    title: 'Result ranking',
    description: 'Rates how useful each search result is for a question, when a chat model reranks the results.',
    group: 'search',
  },
  'conversation-title': {
    title: 'Conversation titles',
    description: 'Writes a short title for each conversation from its first message.',
    group: 'conversations',
  },
  'conversation-labels': {
    title: 'Conversation labels',
    description: 'Chooses up to two of the labels that apply automatically for a conversation, from what each label means.',
    group: 'conversations',
  },
  'eval-judge': {
    title: 'Answer grading',
    description: 'Grades the answers of evaluation runs for faithfulness, relevance and correctness.',
    group: 'evaluations',
  },
};

export function promptInfo(name: string): PromptInfo {
  return KNOWN[name] ?? { title: name, description: 'From your Langfuse project. Knowledge Agent does not use it.', group: 'other' };
}

/** Fixed order; empty groups are left out. */
export function groupPrompts(prompts: readonly PromptSummaryDto[]): Array<{ id: PromptGroupId; label: string; prompts: PromptSummaryDto[] }> {
  return GROUPS.map((group) => ({ ...group, prompts: prompts.filter((prompt) => promptInfo(prompt.name).group === group.id) })).filter(
    (group) => group.prompts.length > 0,
  );
}
