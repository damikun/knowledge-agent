import type { AgentFormErrors, AgentFormField, AgentFormValues } from './agentForm';
import { toSaveRequest, type SaveAgentContext } from './agentForm';

/** ?step= keeps the current one, so reload and back return to it. */
export type WizardStep = 'start' | 'general' | 'behavior' | 'knowledge' | 'tools' | 'review';

export const WIZARD_STEPS: ReadonlyArray<{ id: WizardStep; label: string; title: string; why: string }> = [
  {
    id: 'start',
    label: 'Start',
    title: 'How should it start?',
    why: 'Each starting point fills in sensible settings. You can change all of them now and after you create it.',
  },
  { id: 'general', label: 'General', title: 'Name and language', why: 'How your team recognizes it, and the language it answers in.' },
  { id: 'behavior', label: 'Behavior', title: 'How it talks', why: 'How it sounds to your visitors. You can write more detailed instructions after you create it.' },
  {
    id: 'knowledge',
    label: 'Knowledge',
    title: 'What it knows',
    why: 'It answers only from what you allow here, so visitors get your facts instead of guesses.',
  },
  {
    id: 'tools',
    label: 'Tools and handover',
    title: 'Tools and handover',
    why: 'What it may do while it answers, and when it brings in your team. Your starting point already chose these, so most people just continue.',
  },
  { id: 'review', label: 'Review', title: 'Review and create', why: 'Check the agent in short. You can change everything later on its page.' },
];

/** The numbered steps; review is the last check after them. */
export const NUMBERED_STEPS = WIZARD_STEPS.filter((step) => step.id !== 'review');

// The fields each step shows, so its validation and the API's errors land on the step that can fix them.
const STEP_FIELDS: Readonly<Record<WizardStep, readonly AgentFormField[]>> = {
  start: [],
  general: ['name', 'key', 'description', 'language'],
  behavior: ['instructions'],
  knowledge: ['folderIds'],
  tools: ['tools'],
  review: ['profileName'],
};

/** Every field the wizard has a control for; the API's errors on others are listed on the review step. */
export const WIZARD_FIELDS: readonly AgentFormField[] = Object.values(STEP_FIELDS).flat().filter((field) => field !== 'profileName');

export function stepIndex(step: WizardStep): number {
  return WIZARD_STEPS.findIndex((item) => item.id === step);
}

// Step names from older links.
const OLD_STEPS: Readonly<Record<string, WizardStep>> = { basics: 'general', people: 'tools' };

export function parseStep(value: string | null): WizardStep | null {
  return WIZARD_STEPS.find((step) => step.id === value)?.id ?? (value ? OLD_STEPS[value] : undefined) ?? null;
}

export function stepOfField(field: string): WizardStep {
  return WIZARD_STEPS.find((step) => (STEP_FIELDS[step.id] as readonly string[]).includes(field))?.id ?? 'review';
}

export function stepErrors(
  step: WizardStep,
  values: AgentFormValues,
  hasStart: boolean,
  context: SaveAgentContext,
): AgentFormErrors & { start?: string } {
  if (step === 'start') {
    return hasStart ? {} : { start: 'Choose a starting point.' };
  }
  const { errors } = toSaveRequest(values, context);
  // A key that follows the name is only wrong because the name is missing; one message is enough.
  if (errors.name && !values.keyEdited) {
    delete errors.key;
  }
  const fields = STEP_FIELDS[step];
  return Object.fromEntries(Object.entries(errors).filter(([field]) => fields.includes(field as AgentFormField)));
}

export function firstOpenStepBefore(step: WizardStep, values: AgentFormValues, hasStart: boolean, context: SaveAgentContext): WizardStep | null {
  for (const item of WIZARD_STEPS.slice(0, stepIndex(step))) {
    if (Object.keys(stepErrors(item.id, values, hasStart, context)).length > 0) {
      return item.id;
    }
  }
  return null;
}
