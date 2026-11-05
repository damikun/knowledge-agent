import { Check, Copy } from 'lucide-react';
import { Button } from '../../components/ui/Button';
import { ChoiceCard, ChoiceCardGroup } from '../../components/ui/ChoiceCard';
import { capitalize } from '../../lib/format';
import { useMcpServers } from '../tools/mcp';
import { HANDOVER_POLICIES, scopeSummary, toolLabel, type AgentFormValues } from './agentForm';
import { agentSentence, tonePhrase } from './agentSummary';
import { AGENT_TEMPLATES, type AgentTemplate } from './agentTemplates';
import type { WizardStep } from './agentWizard';
import { languageName } from './languages';
import { appsSummary } from './McpAppsField';
import type { AgentDto, AgentOptionsDto } from './types';

export interface StartStepProps {
  template?: AgentTemplate;
  copyOf?: AgentDto;
  error: string | null;
  onChoose: (next: { template?: string; from?: string }) => void;
  idPrefix: string;
}

export function StartStep({ template, copyOf, error, onChoose, idPrefix }: StartStepProps) {
  const name = `${idPrefix}-start`;
  const hintId = `${idPrefix}-start-hint`;
  return (
    <div className="space-y-2">
      <ChoiceCardGroup label="Starting point" errorId={error ? hintId : undefined}>
        {copyOf ? (
          <ChoiceCard
            name={name}
            checked
            icon={Copy}
            title={<>Copy of {copyOf.name}</>}
            description={<>Every setting of {copyOf.name}, under a name of its own.</>}
            descriptionId={`${name}-copy-pitch`}
          />
        ) : null}
        {AGENT_TEMPLATES.map((item) => (
          <ChoiceCard
            key={item.id}
            name={name}
            checked={template?.id === item.id}
            onChoose={() => onChoose({ template: item.id })}
            icon={item.icon}
            title={item.title}
            description={item.pitch}
            descriptionId={`${name}-${item.id}-pitch`}
          >
            <span className="mt-auto block pt-3">
              <span className="block space-y-1 border-t border-border-subtle pt-3">
                {item.highlights.map((highlight) => (
                  <span key={highlight} className="flex items-center gap-1.5 text-xs text-fg-muted">
                    <Check className="size-3.5 shrink-0 text-success" aria-hidden />
                    {highlight}
                  </span>
                ))}
              </span>
            </span>
          </ChoiceCard>
        ))}
      </ChoiceCardGroup>
      {error ? (
        <p id={hintId} className="text-xs text-danger-text">
          {error}
        </p>
      ) : null}
    </div>
  );
}

export function ReviewStep({
  values,
  tree,
  options,
  onChange,
}: {
  values: AgentFormValues;
  tree: Parameters<typeof scopeSummary>[1];
  options: AgentOptionsDto;
  onChange: (step: WizardStep) => void;
}) {
  const tone = tonePhrase(values.instructions);
  const tools = options.tools.filter((tool) => values.tools.includes(tool)).map(toolLabel);
  const servers = useMcpServers();
  const apps = appsSummary(values.mcpApps, servers.data);
  const rows: Array<{ label: string; value: string; step: WizardStep }> = [
    { label: 'Name', value: values.description.trim() ? `${values.name.trim()}: ${values.description.trim()}` : values.name.trim(), step: 'general' },
    { label: 'Language', value: values.language.trim() ? languageName(values.language) : 'Same as the visitor', step: 'general' },
    { label: 'Tone', value: tone ? capitalize(tone) : 'Set in the instructions', step: 'behavior' },
    {
      label: 'Knows',
      value: `${values.scope === 'all' ? 'All your sources' : scopeSummary(values.folderIds, tree)}${values.allowGeneralKnowledge ? ', plus general knowledge' : ''}`,
      step: 'knowledge',
    },
    { label: 'Tools', value: tools.length > 0 ? tools.join(', ') : 'None, so it cannot search your sources', step: 'tools' },
    ...(apps ? [{ label: 'Connected apps', value: `Uses ${apps}`, step: 'tools' as const }] : []),
    { label: 'Talk to a person', value: HANDOVER_POLICIES.find((policy) => policy.value === values.handoverPolicy)?.label ?? '', step: 'tools' },
  ];
  return (
    <section aria-labelledby="review-heading" className="rounded-2xl border border-border bg-surface shadow-card">
      <div className="border-b border-border-subtle p-5">
        <h2 id="review-heading" className="text-section text-fg">
          In short
        </h2>
        <p data-testid="agent-sentence" className="mt-1 text-sm text-fg-secondary">
          {agentSentence(values)}
        </p>
      </div>
      <dl className="divide-y divide-border-subtle px-5">
        {rows.map((row) => (
          <div key={row.label} className="flex items-start gap-4 py-3">
            <dt className="w-32 shrink-0 text-small text-fg-muted">{row.label}</dt>
            <dd className="min-w-0 flex-1 text-small text-fg">{row.value}</dd>
            <dd className="shrink-0">
              <Button size="sm" variant="ghost" onClick={() => onChange(row.step)} aria-label={`Change ${row.label.toLowerCase()}`}>
                Change
              </Button>
            </dd>
          </div>
        ))}
      </dl>
    </section>
  );
}
