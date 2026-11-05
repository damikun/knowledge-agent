import { clsx } from 'clsx';
import { ArrowRight, Bot } from 'lucide-react';
import { Fragment } from 'react';
import { Link } from 'react-router';
import type { AgentDto } from '../../agents/types';
import type { WidgetFormValues } from '../widgetForm';
import { agentFacts, widgetChanges, type WidgetChange } from './widgetChanges';
import { useReturnState } from '../../../lib/returnTo';

const WIDGET_ROLE_HINT = 'The agent decides what it knows and does. This widget decides where people meet it, and can switch agent features off.';

function findAgent(agents: readonly AgentDto[], agentId: string): AgentDto | undefined {
  return agents.find((candidate) => candidate.id.toLowerCase() === agentId.toLowerCase());
}

export function AnsweredBy({ agents, agentId, className }: { agents: readonly AgentDto[]; agentId: string; className?: string }) {
  const returnState = useReturnState();
  const agent = findAgent(agents, agentId);
  return (
    <div data-testid="answered-by" className={clsx('border-b border-border-subtle px-4 py-3', className)}>
      <p className="flex min-w-0 items-center gap-1.5 text-small">
        <Bot className="size-4 shrink-0 text-fg-muted" aria-hidden />
        <span className="shrink-0 text-fg-secondary">Answered by:</span>{' '}
        {agent ? (
          <Link
            to={`/agents/${agent.id}`}
            state={returnState}
            className="inline-flex min-w-0 items-center gap-1 font-medium text-accent-text hover:underline"
          >
            <span className="truncate">{agent.name}</span>
            <ArrowRight className="size-3.5 shrink-0" aria-hidden />
          </Link>
        ) : (
          <span className="text-fg-muted">None yet. Choose one under General.</span>
        )}
      </p>
    </div>
  );
}

function Rows({ title, facts }: { title: string; facts: readonly WidgetChange[] }) {
  return (
    <>
      <dt className="col-span-2 mt-2 text-xs font-medium text-fg-secondary first:mt-0">{title}</dt>
      {facts.map((fact) => (
        <Fragment key={`${title}-${fact.label}`}>
          <dt className="text-fg-muted">{fact.label}</dt>
          <dd className={clsx('min-w-0', fact.limited ? 'text-warning-text' : 'text-fg')}>{fact.value}</dd>
        </Fragment>
      ))}
    </>
  );
}

export function AgentAndWidget({ agents, values }: { agents: readonly AgentDto[]; values: WidgetFormValues }) {
  const agent = findAgent(agents, values.agentId);
  if (!agent) {
    return null;
  }
  return (
    <div data-testid="widget-changes" className="rounded-xl border border-border-subtle bg-surface-subtle p-3 text-xs">
      <p className="text-fg-muted">{WIDGET_ROLE_HINT}</p>
      <dl className="mt-2 grid grid-cols-[auto_1fr] gap-x-3 gap-y-1">
        <Rows title="From the agent" facts={agentFacts(agent)} />
        <Rows title="This widget" facts={widgetChanges(values, agent)} />
      </dl>
    </div>
  );
}
