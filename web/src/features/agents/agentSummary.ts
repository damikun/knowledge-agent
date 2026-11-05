import { pluralize } from '../../lib/format';
import { HANDOVER_POLICIES, type AgentFormValues } from './agentForm';
import { TONES, toneOf } from './agentTemplates';

export interface SummaryContext {
  widgetNames?: readonly string[];
}

/** "Demo shop", "Demo shop and Docs help", "Demo shop and 2 more". */
export function namesList(names: readonly string[]): string {
  if (names.length <= 2) {
    return names.join(' and ');
  }
  return `${names[0]} and ${names.length - 1} more`;
}

function knowledgePhrase(values: Pick<AgentFormValues, 'scope' | 'folderIds'>): string {
  return values.scope === 'all' ? 'answers from all your sources' : `answers from ${pluralize(values.folderIds.length, 'folder')}`;
}

function languagePhrase(language: string): string {
  return language.trim() ? `in ${language.trim()}` : "in the visitor's language";
}

export function tonePhrase(instructions: string): string | null {
  const tone = toneOf(instructions);
  if (tone === 'own' && !instructions.trim()) {
    return null;
  }
  return TONES.find((item) => item.value === tone)?.summary ?? null;
}

/** E.g. "This agent answers from 3 folders in English, friendly tone, used by Demo shop." */
export function agentSentence(values: AgentFormValues, { widgetNames }: SummaryContext = {}): string {
  const parts: string[] = [];
  const tone = tonePhrase(values.instructions);
  if (tone) {
    parts.push(tone);
  }
  if (values.allowGeneralKnowledge) {
    parts.push('fills gaps from general knowledge');
  }
  parts.push(HANDOVER_POLICIES.find((policy) => policy.value === values.handoverPolicy)?.summary ?? '');
  if (widgetNames) {
    parts.push(widgetNames.length > 0 ? `used by ${namesList(widgetNames)}` : 'not on your website yet');
  }
  const head = `This agent ${knowledgePhrase(values)} ${languagePhrase(values.language)}`;
  return `${[head, ...parts.filter(Boolean)].join(', ')}.`;
}

export function playgroundLink(agent: { id: string }): string {
  return `/playground?agentId=${encodeURIComponent(agent.id)}`;
}
