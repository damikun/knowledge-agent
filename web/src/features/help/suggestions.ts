import type { HelpSetupDto } from './types';

const MAX = 4;

// Keyed by the first path segment.
const BY_PAGE: Readonly<Record<string, readonly string[]>> = {
  home: ['What are the setup steps?', 'How do I test the agent before visitors do?'],
  inbox: ['How do I take over a conversation?', 'How do I hand a conversation back to the assistant?', 'How do labels work?'],
  contacts: ['How do I save a segment?', 'How do I identify signed-in users?', 'What are contact attributes?'],
  feedback: ['Where do the ratings come from?', 'How do visitors reach a person?'],
  analytics: ['Why could the agent not answer some questions?', 'How are conversations labeled?'],
  playground: ['How do I test a widget before it goes live?', 'Why does the agent not find anything?'],
  agents: ['How do I create an agent?', 'How do I limit an agent to some folders?', 'How do I give an agent tools from an MCP server?'],
  widgets: ['How do I put the widget on my site?', 'How do I limit the sites that may show it?', 'How do I identify signed-in users?'],
  sources: ['How do I add a website?', 'Why did a source fail?', 'How do folders work?'],
  prompts: ['Where do I edit the prompts?'],
  evals: ['How do I run an evaluation?', 'How do I add a question from a conversation?'],
  tools: ['How do I add an MCP server?', 'How do I let an agent use a tool?'],
  notifications: ['How do I connect Slack?', 'How do I send alerts by email?', 'How does email handover work?'],
  team: ['Who can change settings?'],
  models: ['Which provider should I pick?', 'How do I add an API key?'],
  system: ['How do I set the public address?', 'How do I turn the help assistant off?', 'What runs in the background?'],
};

const GENERAL = ['What are the setup steps?', 'How do I put the widget on my site?', 'How do visitors reach a person?'];

const SUPPORT = ['How do I take over a conversation?', 'How do I save a segment?', 'Where do the ratings come from?'];
const SUPPORT_PAGES = new Set(['inbox', 'contacts', 'feedback']);

export function pageSegment(pathname: string): string {
  return pathname.split(/[/?#]/).find((part) => part !== '')?.toLowerCase() ?? '';
}

/** The next setup step first (admins), then the page's questions, then general ones; at most four. */
export function helpSuggestions(pathname: string, setup: HelpSetupDto | null, admin: boolean): string[] {
  const segment = pageSegment(pathname);
  const questions: string[] = [];
  if (admin && setup) {
    if (!setup.chatModel || !setup.embeddingModel) {
      questions.push('How do I connect a model?');
    } else if (!setup.sources) {
      questions.push('How do I add knowledge?');
    } else if (!setup.agent) {
      questions.push('How do I create an agent?');
    } else if (!setup.widget) {
      questions.push('How do I put the widget on my site?');
    }
  }
  const page = admin || SUPPORT_PAGES.has(segment) ? (BY_PAGE[segment] ?? []) : [];
  questions.push(...page, ...(admin ? GENERAL : SUPPORT));
  return [...new Set(questions)].slice(0, MAX);
}
