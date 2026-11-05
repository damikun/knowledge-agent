import { Badge } from '../../../components/ui/Badge';
import type { McpToolDto } from './types';
import { policyLabel, toolEffect, toolLabel } from './words';

export interface McpToolListProps {
  tools: readonly McpToolDto[];
  /** Names the list for screen readers. */
  label: string;
  /** Shows the default policy for each tool. */
  showDefaults?: boolean;
}

export function McpToolList({ tools, label, showDefaults = true }: McpToolListProps) {
  if (tools.length === 0) {
    return <p className="text-small text-fg-muted">No tools yet. Test the connection to load them.</p>;
  }
  return (
    <ul aria-label={label} className="divide-y divide-border-subtle rounded-xl border border-border">
      {tools.map((tool) => {
        const effect = toolEffect(tool);
        return (
          <li key={tool.name} className="space-y-1 px-3 py-2.5">
            <div className="flex flex-wrap items-center gap-x-2 gap-y-1">
              <span className="text-sm font-medium text-fg">{toolLabel(tool)}</span>
              <Badge tone={effect.tone}>{effect.label}</Badge>
              {showDefaults ? <span className="text-xs text-fg-muted">By default: {policyLabel(tool.defaultPolicy).toLowerCase()}</span> : null}
            </div>
            {tool.description ? <p className="line-clamp-2 text-small text-fg-secondary">{tool.description}</p> : null}
            <p className="font-mono text-xs text-fg-muted">{tool.name}</p>
          </li>
        );
      })}
    </ul>
  );
}
