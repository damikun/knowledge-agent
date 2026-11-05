import { Bot, X } from 'lucide-react';
import { Link } from 'react-router';
import { fieldAria } from '../../components/ui/aria';
import { Button } from '../../components/ui/Button';
import { Field } from '../../components/ui/Field';
import { Select } from '../../components/ui/Select';
import { Switch } from '../../components/ui/Switch';
import { sameId } from '../../lib/ids';
import type { Guid } from '../../lib/types';
import { MAX_SPECIALISTS, MAX_WHEN_TO_USE_LENGTH } from './agentForm';
import type { FieldKit } from './agentFields';
import type { AgentDto } from './types';
import { useReturnState } from '../../lib/returnTo';

/** Whether `start` hands off to `target`, directly or through its own specialists; such a pair would loop. */
function handsOffTo(agents: readonly AgentDto[], start: Guid, target: Guid): boolean {
  const seen = new Set<string>();
  const pending = [start];
  while (pending.length > 0) {
    const current = pending.pop()!;
    if (sameId(current, target)) {
      return true;
    }
    if (seen.has(current.toLowerCase())) {
      continue;
    }
    seen.add(current.toLowerCase());
    const agent = agents.find((item) => sameId(item.id, current));
    pending.push(...(agent?.specialists ?? []).map((specialist) => specialist.agentId));
  }
  return false;
}

function teamsWith(agents: readonly AgentDto[], agent: AgentDto): AgentDto[] {
  return agents.filter((other) => !sameId(other.id, agent.id) && (other.specialists ?? []).some((specialist) => sameId(specialist.agentId, agent.id)));
}

export function SpecialistsField({ kit, agent, agents }: { kit: FieldKit; agent: AgentDto; agents: readonly AgentDto[] }) {
  const returnState = useReturnState();
  const { values, set, errorsFor, id } = kit;
  const chosen = values.specialists ?? [];
  const errors = errorsFor('specialists');
  const candidates = agents.filter((other) => !other.isBuiltIn && !sameId(other.id, agent.id) && !chosen.some((item) => sameId(item.agentId, other.id)));
  const memberOf = teamsWith(agents, agent);

  const update = (index: number, whenToUse: string) =>
    set(
      'specialists',
      chosen.map((item, at) => (at === index ? { ...item, whenToUse } : item)),
      ['specialists'],
    );
  const remove = (index: number) =>
    set(
      'specialists',
      chosen.filter((_, at) => at !== index),
      ['specialists'],
    );
  const add = (agentId: Guid) => {
    const picked = agents.find((other) => sameId(other.id, agentId));
    set('specialists', [...chosen, { agentId, whenToUse: picked?.description?.slice(0, MAX_WHEN_TO_USE_LENGTH) ?? '' }], ['specialists']);
  };

  return (
    <div className="space-y-4">
      {memberOf.length > 0 ? (
        <p className="text-small text-fg-secondary" data-testid="specialist-of">
          It is a specialist of {memberOf.map((other) => other.name).join(', ')}. When one of them hands it a conversation, it can only hand
          back; its own specialists are used only where it answers first.
        </p>
      ) : null}

      {chosen.length > 0 ? (
        <ul aria-label="Specialists" className="space-y-3">
          {chosen.map((item, index) => {
            const specialist = agents.find((other) => sameId(other.id, item.agentId));
            const name = specialist?.name ?? 'Deleted agent';
            const inputId = id(`when-${index}`);
            const missing = errors !== undefined && !item.whenToUse.trim();
            return (
              <li key={item.agentId} className="space-y-3 rounded-xl border border-border-subtle bg-surface-subtle p-4">
                <div className="flex items-start gap-3">
                  <span aria-hidden className="flex size-8 shrink-0 items-center justify-center rounded-lg bg-surface text-fg-muted">
                    <Bot className="size-4" />
                  </span>
                  <div className="min-w-0 flex-1">
                    {specialist ? (
                      <Link to={`/agents/${specialist.id}`} state={returnState} className="text-sm font-medium text-accent-text hover:underline">
                        {name}
                      </Link>
                    ) : (
                      <p className="text-sm font-medium text-fg">{name}</p>
                    )}
                    {specialist?.description ? <p className="truncate text-xs text-fg-muted">{specialist.description}</p> : null}
                  </div>
                  <Button variant="ghost" size="icon-sm" icon={X} aria-label={`Remove ${name}`} onClick={() => remove(index)} />
                </div>
                <Field
                  label="When to hand off"
                  htmlFor={inputId}
                  errors={missing ? errors : undefined}
                  hint="One line the agent reads, such as: Questions about invoices, payments and refunds."
                >
                  <input
                    {...fieldAria(inputId, missing ? errors : undefined, true)}
                    maxLength={MAX_WHEN_TO_USE_LENGTH}
                    autoComplete="off"
                    value={item.whenToUse}
                    onChange={(event) => update(index, event.target.value)}
                    className="field"
                  />
                </Field>
              </li>
            );
          })}
        </ul>
      ) : (
        <p className="text-small text-fg-secondary">No specialists yet, so it answers every question itself.</p>
      )}

      {errors && !chosen.some((item) => !item.whenToUse.trim()) ? (
        <p className="text-xs text-danger-text" role="alert">
          {errors.join(' ')}
        </p>
      ) : null}

      {candidates.length > 0 && chosen.length < MAX_SPECIALISTS ? (
        <Field label="Add a specialist" htmlFor={id('add-specialist')} hint="Agents that already hand off to this one cannot be added: the two would pass a conversation back and forth.">
          <Select
            id={id('add-specialist')}
            value=""
            placeholder="Choose an agent"
            className="max-w-80"
            onChange={add}
            options={candidates.map((other) => {
              const loops = handsOffTo(agents, other.id, agent.id);
              return { value: other.id, label: other.name, disabled: loops, hint: loops ? 'Hands off to this agent' : undefined };
            })}
          />
        </Field>
      ) : candidates.length === 0 && chosen.length === 0 ? (
        <p className="text-small text-fg-secondary">
          Create another agent first, such as Billing or Orders, with its own instructions and knowledge, then add it here.{' '}
          <Link to="/agents/new" className="font-medium text-accent-text hover:underline">
            New agent
          </Link>
        </p>
      ) : null}

      <div className="space-y-1.5 border-t border-border-subtle pt-4">
        <Switch
          checked={values.showSpecialistName === true}
          onChange={(checked) => set('showSpecialistName', checked)}
          label="Show the specialist's name"
          describedBy={id('show-name-hint')}
        />
        <p id={id('show-name-hint')} className="text-xs text-fg-muted">
          On: visitors see a notice such as “Billing assistant joined”, and the specialist answers under its own name. Off: visitors see one
          assistant, and only your team sees the hand-off in the Inbox.
        </p>
      </div>
    </div>
  );
}
