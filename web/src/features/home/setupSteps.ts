import type { SetupStep } from '../../components/ui/SetupChecklist';
import { PROVIDERS_PATH } from '../models/providerSettings';

export interface SetupFacts {
  modelConnected: boolean | undefined;
  hasSources: boolean | undefined;
  hasAgent: boolean | undefined;
  firstWidgetId: string | null | undefined;
  widgetLive: boolean | undefined;
}

export function setupSteps(facts: SetupFacts): SetupStep[] | null {
  const { modelConnected, hasSources, hasAgent, firstWidgetId, widgetLive } = facts;
  if (modelConnected === undefined || hasSources === undefined || hasAgent === undefined || firstWidgetId === undefined || widgetLive === undefined) {
    return null;
  }
  const steps: Array<Omit<SetupStep, 'state'> & { done: boolean }> = [
    {
      id: 'model',
      title: 'Connect a model',
      description: 'Add an API key, so the agent can answer and read your sources.',
      done: modelConnected,
      action: { label: 'Add an API key', to: PROVIDERS_PATH },
    },
    {
      id: 'knowledge',
      title: 'Add knowledge',
      description: 'A website, files or text the agent answers from.',
      done: hasSources,
      action: { label: 'Add a source', to: '/sources' },
    },
    {
      id: 'agent',
      title: 'Create an agent',
      description: 'Give it a persona, the knowledge it may use and its tools.',
      done: hasAgent,
      action: { label: 'Create an agent', to: '/agents/new' },
    },
    {
      id: 'widget',
      title: 'Put a widget on your site',
      description: firstWidgetId
        ? 'Paste the snippet into your site. This step is done after the first conversation.'
        : 'Create a widget for your agent, then paste its snippet into your site.',
      done: widgetLive,
      action: firstWidgetId ? { label: 'Get the snippet', to: `/widgets/${firstWidgetId}` } : { label: 'Create a widget', to: '/widgets/new' },
    },
  ];
  const current = steps.findIndex((step) => !step.done);
  return steps.map(({ done, ...step }, index) => ({ ...step, state: done ? 'done' : index === current ? 'current' : 'todo' }));
}
