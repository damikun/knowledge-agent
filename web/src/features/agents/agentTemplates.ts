import {
  BookOpen,
  BriefcaseBusiness,
  FileSearch,
  Headset,
  HandCoins,
  MessageSquareHeart,
  PenLine,
  Search,
  Telescope,
  TextSearch,
  Wrench,
  type LucideIcon,
} from 'lucide-react';
import type { HandoverPolicy } from './types';

export type ToneId = 'friendly' | 'professional' | 'brief' | 'own';

// The first line of the instructions; the tone control finds and swaps it.
export const TONE_SENTENCES: Readonly<Record<Exclude<ToneId, 'own'>, string>> = {
  friendly: 'Be warm and friendly, and write the way a helpful person talks.',
  professional: 'Be polite and professional, and keep a calm, factual tone.',
  brief: 'Be brief: answer in a few sentences and skip small talk.',
};

export const AGENT_FRAMING =
  'An agent is the assistant your visitors talk to: what it knows, how it speaks, what it may do, and when it brings in your team.';

export const TONES: ReadonlyArray<{ value: ToneId; label: string; summary: string; example: string }> = [
  { value: 'friendly', label: 'Friendly', summary: 'friendly tone', example: '"Happy to help! Your parcel left today, so it should be with you tomorrow."' },
  { value: 'professional', label: 'Professional', summary: 'professional tone', example: '"Your order was shipped today. Delivery usually takes one working day."' },
  { value: 'brief', label: 'Brief', summary: 'brief answers', example: '"Shipped today. Arrives tomorrow."' },
  { value: 'own', label: 'Own words', summary: 'tone in your own words', example: 'Describe the tone yourself in the instructions.' },
];

/** The tone whose sentence the instructions contain; "own" when none does. */
export function toneOf(instructions: string): ToneId {
  const found = (Object.keys(TONE_SENTENCES) as Array<keyof typeof TONE_SENTENCES>).find((tone) => instructions.includes(TONE_SENTENCES[tone]));
  return found ?? 'own';
}

/** The instructions with the tone sentence swapped; "own" removes it, so people describe the tone themselves. */
export function withTone(instructions: string, tone: ToneId): string {
  let rest = instructions;
  for (const sentence of Object.values(TONE_SENTENCES)) {
    rest = rest.split(`${sentence}\n`).join('').split(sentence).join('');
  }
  rest = rest.replace(/^\s+/, '');
  if (tone === 'own') {
    return rest;
  }
  return rest ? `${TONE_SENTENCES[tone]}\n${rest}` : TONE_SENTENCES[tone];
}

export interface InstructionSnippet {
  label: string;
  text: string;
}

export const INSTRUCTION_SNIPPETS: readonly InstructionSnippet[] = [
  {
    label: 'Stay on topic',
    text: 'Only answer questions about our products and services. For anything else, say politely that you can only help with those.',
  },
  { label: 'Say when you do not know', text: 'If the sources do not answer a question, say so plainly instead of guessing.' },
  { label: 'Offer a next step', text: 'End each answer with a clear next step, such as a link to the right page.' },
];

export function withSnippet(instructions: string, snippet: InstructionSnippet): string {
  const trimmed = instructions.replace(/\s+$/, '');
  return trimmed ? `${trimmed}\n${snippet.text}` : snippet.text;
}

/** Long prompts get harder for the model to follow. */
export function instructionsGuidance(length: number, max: number): { text: string; long: boolean } {
  if (length === 0) {
    return { text: 'A few clear sentences are enough. Your knowledge does the rest.', long: false };
  }
  const count = `${length.toLocaleString('en')} of ${max.toLocaleString('en')} characters.`;
  if (length > 3_000) {
    return { text: `${count} Long instructions are harder to follow: keep the rules that matter most.`, long: true };
  }
  return { text: `${count} Most good instructions are 200 to 1,500.`, long: false };
}

// Tools, in plain words. Ids the portal does not know yet still show, under Other.
export interface ToolInfo {
  label: string;
  effect: string;
  icon: LucideIcon;
}

const TOOL_INFO: Readonly<Record<string, ToolInfo>> = {
  search_knowledge_base: { label: 'Search your knowledge', effect: 'Finds the passages that answer a question.', icon: Search },
  grep_sources: { label: 'Find exact words', effect: 'Finds every place a name, number or phrase appears.', icon: TextSearch },
  find_sources: { label: 'Look up a document', effect: 'Finds a document by its title or web address.', icon: FileSearch },
  read_source: { label: 'Read a whole document', effect: 'Reads a document from start to end when a passage is not enough.', icon: BookOpen },
  delegate_research: { label: 'Research hard questions', effect: 'Hands a hard question to a helper that searches in several steps.', icon: Telescope },
  record_feedback: { label: 'Note feedback', effect: 'Saves praise and complaints people share, for the Feedback page.', icon: MessageSquareHeart },
};

export function toolInfo(tool: string): ToolInfo {
  return TOOL_INFO[tool] ?? { label: tool, effect: 'A tool this portal does not describe yet.', icon: Wrench };
}

export interface ToolGroup {
  id: string;
  title: string;
  description: string;
  tools: string[];
}

const GROUPS: ReadonlyArray<Omit<ToolGroup, 'tools'> & { members: readonly string[] }> = [
  { id: 'answers', title: 'Find answers', description: 'How it looks things up in your knowledge.', members: ['search_knowledge_base', 'grep_sources', 'find_sources', 'read_source'] },
  { id: 'research', title: 'Research', description: 'For questions one search cannot answer.', members: ['delegate_research'] },
  { id: 'feedback', title: 'Collect feedback', description: 'So you learn what people think.', members: ['record_feedback'] },
];

export function groupTools(available: readonly string[]): ToolGroup[] {
  const known = new Set(GROUPS.flatMap((group) => group.members));
  const groups = GROUPS.map(({ members, ...group }) => ({ ...group, tools: available.filter((tool) => members.includes(tool)) }));
  const other = available.filter((tool) => !known.has(tool));
  return [...groups, { id: 'other', title: 'Other', description: 'Tools added since this page was written.', tools: other }].filter((group) => group.tools.length > 0);
}

type AgentTemplateId = 'support' | 'sales' | 'helpdesk' | 'blank';

export interface AgentTemplate {
  id: AgentTemplateId;
  title: string;
  pitch: string;
  icon: LucideIcon;
  highlights: string[];
  name: string;
  description: string;
  instructions: string;
  /** Null turns on every tool. */
  tools: string[] | null;
  handoverPolicy: HandoverPolicy;
  allowGeneralKnowledge: boolean;
}

const FIND_ANSWERS = ['search_knowledge_base', 'grep_sources', 'find_sources', 'read_source'];

export const AGENT_TEMPLATES: readonly AgentTemplate[] = [
  {
    id: 'support',
    title: 'Customer support',
    pitch: 'Answers customers from your help content and brings in your team when it cannot help.',
    icon: Headset,
    highlights: ['Friendly tone', 'Offers a person when unsure', 'Notes feedback'],
    name: 'Customer support',
    description: 'Answers customer questions from our help content and brings in the team when needed.',
    instructions: [
      TONE_SENTENCES.friendly,
      'You help our customers with questions about orders, delivery, returns and our products.',
      'Answer from the sources and point to the page the answer comes from.',
      'If a question is about one order or account, ask for the order number first.',
    ].join('\n'),
    tools: [...FIND_ANSWERS, 'record_feedback'],
    handoverPolicy: 'whenUnsure',
    allowGeneralKnowledge: false,
  },
  {
    id: 'sales',
    title: 'Sales questions',
    pitch: 'Helps people who are about to buy: products, plans and prices, then hands them to sales.',
    icon: HandCoins,
    highlights: ['Professional tone', 'Never invents prices', 'Brings in sales when asked'],
    name: 'Sales assistant',
    description: 'Answers questions about products, plans and prices, and connects buyers with sales.',
    instructions: [
      TONE_SENTENCES.professional,
      'You answer questions from people who are thinking about buying: products, plans, prices and how we compare.',
      'Never invent prices, discounts or delivery dates. Quote only what the sources say.',
      'When someone is ready to buy or wants a quote, offer to connect them with our sales team.',
    ].join('\n'),
    tools: [...FIND_ANSWERS, 'record_feedback'],
    handoverPolicy: 'onRequest',
    allowGeneralKnowledge: false,
  },
  {
    id: 'helpdesk',
    title: 'Internal helpdesk',
    pitch: 'Answers your own team about policies, tools and processes, with the document to check.',
    icon: BriefcaseBusiness,
    highlights: ['Brief answers', 'Researches hard questions', 'Names its sources'],
    name: 'Internal helpdesk',
    description: 'Answers questions from our team about policies, tools and processes.',
    instructions: [
      TONE_SENTENCES.brief,
      'You answer questions from our own team about internal policies, tools and processes.',
      'Name the document an answer comes from, so people can check the details.',
    ].join('\n'),
    tools: [...FIND_ANSWERS, 'delegate_research'],
    handoverPolicy: 'onRequest',
    allowGeneralKnowledge: false,
  },
  {
    id: 'blank',
    title: 'Start from scratch',
    pitch: 'An empty agent with every tool on. Write its purpose and instructions yourself.',
    icon: PenLine,
    highlights: ['Every tool on', 'Brings in a person when asked'],
    name: '',
    description: '',
    instructions: '',
    tools: null,
    handoverPolicy: 'onRequest',
    allowGeneralKnowledge: false,
  },
];

export function findTemplate(id: string | null): AgentTemplate | undefined {
  return AGENT_TEMPLATES.find((template) => template.id === id);
}
