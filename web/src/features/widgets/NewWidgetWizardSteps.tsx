import { Bot, Plus } from 'lucide-react';
import { Link } from 'react-router';
import { Button, buttonClass } from '../../components/ui/Button';
import { ChoiceCard, ChoiceCardGroup } from '../../components/ui/ChoiceCard';
import { Card, EmptyState } from '../../components/ui/feedback';
import { TextLink } from '../../components/ui/TextLink';
import { useReturnState } from '../../lib/returnTo';
import type { AgentDto } from '../agents/types';
import { splitLines, type WidgetFormValues } from './widgetForm';
import { PRESET_LABELS } from './widgetTexts';
import type { WidgetWizardStep } from './widgetWizard';

const PERSON_CHANNELS: Readonly<Record<string, string>> = {
  portal: 'the Inbox',
  email: 'email and the Inbox',
  slack: 'Slack and the Inbox',
};

interface AgentStepProps {
  agents: readonly AgentDto[];
  value: string;
  errors: string[] | undefined;
  onChoose: (agentId: string) => void;
  idPrefix: string;
}

export function AgentStep({ agents, value, errors, onChoose, idPrefix }: AgentStepProps) {
  const returnState = useReturnState();
  const name = `${idPrefix}-agent`;
  const errorId = `${name}-error`;
  if (agents.length === 0) {
    return (
      <Card>
        <EmptyState
          icon={Bot}
          title="No agent to answer yet"
          description="A widget needs an agent of its own, with only the sources visitors may see. Create one first, then come back."
          action={
            <Link to="/agents" state={returnState} className={buttonClass({ variant: 'primary' })}>
              <Plus className="size-4" aria-hidden />
              Create an agent
            </Link>
          }
        />
      </Card>
    );
  }
  return (
    <div className="space-y-3">
      <ChoiceCardGroup label="Agent" errorId={errors ? errorId : undefined}>
        {agents.map((agent) => (
          <ChoiceCard
            key={agent.id}
            name={name}
            checked={value === agent.id}
            onChoose={() => onChoose(agent.id)}
            icon={Bot}
            title={agent.name}
            titleId={`${name}-${agent.id}-name`}
            description={agent.description}
            descriptionId={`${name}-${agent.id}-pitch`}
          />
        ))}
      </ChoiceCardGroup>
      {errors ? (
        <p id={errorId} className="text-xs text-danger-text">
          {errors.join(' ')}
        </p>
      ) : null}
      <p className="text-small text-fg-secondary">
        None fits?{' '}
        <TextLink to="/agents" state={returnState}>
          Create an agent
        </TextLink>{' '}
        with only the sources visitors may see, then come back.
      </p>
    </div>
  );
}

export function ReviewStep({
  values,
  agents,
  onChange,
}: {
  values: WidgetFormValues;
  agents: readonly AgentDto[];
  onChange: (step: WidgetWizardStep) => void;
}) {
  const agent = agents.find((candidate) => candidate.id === values.agentId);
  const sites = splitLines(values.allowedOrigins);
  const rows: Array<{ label: string; value: string; step: WidgetWizardStep }> = [
    { label: 'Agent', value: agent?.name ?? 'Unknown agent', step: 'agent' },
    { label: 'Name', value: values.name.trim(), step: 'sites' },
    { label: 'Sites', value: sites.length > 0 ? sites.join(', ') : 'Any site. Add yours so the chat only runs there.', step: 'sites' },
    {
      label: 'Look',
      value: `${PRESET_LABELS[values.preset]?.label ?? values.preset}, brand color ${values.brandColor.trim().toLowerCase()}`,
      step: 'look',
    },
    {
      label: 'Talk to a person',
      value: values.handoverEnabled ? `On. Requests go to ${PERSON_CHANNELS[values.channel] ?? values.channel}.` : 'Off on this widget',
      step: 'person',
    },
  ];
  return (
    <section aria-labelledby="review-heading" className="rounded-2xl border border-border bg-surface shadow-card">
      <div className="border-b border-border-subtle p-5">
        <h2 id="review-heading" className="text-section text-fg">
          In short
        </h2>
        <p className="mt-1 text-sm text-fg-secondary">After you create it, the editor shows the snippet to paste into your site.</p>
      </div>
      <dl className="divide-y divide-border-subtle px-5">
        {rows.map((row) => (
          <div key={row.label} className="flex items-start gap-4 py-3">
            <dt className="w-32 shrink-0 text-small text-fg-muted">{row.label}</dt>
            <dd className="min-w-0 flex-1 text-small break-words text-fg">{row.value}</dd>
            <dd className="shrink-0">
              <Button size="sm" variant="ghost" onClick={() => onChange(row.step)} aria-label={`Edit ${row.label.toLowerCase()}`}>
                Edit
              </Button>
            </dd>
          </div>
        ))}
      </dl>
    </section>
  );
}
