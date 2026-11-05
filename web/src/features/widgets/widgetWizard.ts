import { validateWidget, type WidgetField, type WidgetFormErrors, type WidgetFormValues } from './widgetForm';

/** ?step= keeps the current step, so Back returns to it. */
export type WidgetWizardStep = 'agent' | 'sites' | 'look' | 'person' | 'review';

export const WIDGET_WIZARD_STEPS: ReadonlyArray<{ id: WidgetWizardStep; label: string; title: string; why: string }> = [
  {
    id: 'agent',
    label: 'Agent',
    title: 'Which agent answers?',
    why: 'The widget answers visitors with one of your agents, and shows only what that agent may search.',
  },
  { id: 'sites', label: 'Name and sites', title: 'Name and sites', why: 'A name your team recognizes, and the websites where the chat may run.' },
  {
    id: 'look',
    label: 'Look',
    title: 'How it looks',
    why: 'Pick a theme and your brand color. You can change every detail in the editor after you create it.',
  },
  {
    id: 'person',
    label: 'A person',
    title: 'Talk to a person',
    why: 'Whether visitors can ask for someone from your team, and where their requests go.',
  },
  { id: 'review', label: 'Review', title: 'Review and create', why: 'Check the widget in short. You can change everything later in its editor.' },
];

export const NUMBERED_WIDGET_STEPS = WIDGET_WIZARD_STEPS.filter((step) => step.id !== 'review');

// So a step's check and the API's errors land on the step that can fix them.
const STEP_FIELDS: Readonly<Record<WidgetWizardStep, readonly WidgetField[]>> = {
  agent: ['agentId'],
  sites: ['name', 'settings.security.allowedOrigins'],
  look: ['settings.appearance.preset', 'settings.appearance.brandColor'],
  person: ['settings.handover.channel', 'settings.handover.emailTo', 'settings.handover.slackChannelId'],
  review: [],
};

export function widgetStepIndex(step: WidgetWizardStep): number {
  return WIDGET_WIZARD_STEPS.findIndex((item) => item.id === step);
}

export function parseWidgetStep(value: string | null): WidgetWizardStep | null {
  return WIDGET_WIZARD_STEPS.find((step) => step.id === value)?.id ?? null;
}

/** Fields no step shows belong to review. */
export function widgetStepOfField(field: string): WidgetWizardStep {
  return WIDGET_WIZARD_STEPS.find((step) => (STEP_FIELDS[step.id] as readonly string[]).includes(field))?.id ?? 'review';
}

export function widgetStepErrors(step: WidgetWizardStep, values: WidgetFormValues): WidgetFormErrors {
  const fields = STEP_FIELDS[step];
  return Object.fromEntries(Object.entries(validateWidget(values)).filter(([field]) => fields.includes(field as WidgetField)));
}

export function firstOpenWidgetStepBefore(step: WidgetWizardStep, values: WidgetFormValues): WidgetWizardStep | null {
  for (const item of WIDGET_WIZARD_STEPS.slice(0, widgetStepIndex(step))) {
    if (Object.keys(widgetStepErrors(item.id, values)).length > 0) {
      return item.id;
    }
  }
  return null;
}
