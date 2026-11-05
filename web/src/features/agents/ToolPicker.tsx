import { clsx } from 'clsx';
import { groupTools, toolInfo } from './agentTemplates';

export interface ToolPickerProps {
  idPrefix: string;
  available: readonly string[];
  selected: readonly string[];
  onToggle: (tool: string, on: boolean) => void;
  describedBy?: string;
}

export function ToolPicker({ idPrefix, available, selected, onToggle, describedBy }: ToolPickerProps) {
  const groups = groupTools(available);
  return (
    <div className="space-y-5">
      {groups.map((group) => {
        const headingId = `${idPrefix}-group-${group.id}`;
        return (
          <div key={group.id} role="group" aria-labelledby={headingId} aria-describedby={describedBy} className="space-y-2">
            <p id={headingId} className="text-small font-medium text-fg">
              {group.title}
              <span className="ml-2 font-normal text-fg-muted">{group.description}</span>
            </p>
            <div className="grid gap-2 @min-[34rem]:grid-cols-2">
              {group.tools.map((tool) => {
                const { label, effect, icon: Icon } = toolInfo(tool);
                const on = selected.includes(tool);
                const labelId = `${idPrefix}-tool-${tool}`;
                return (
                  <label
                    key={tool}
                    className={clsx(
                      'flex cursor-pointer items-start gap-3 rounded-lg border p-3',
                      on ? 'border-accent-edge bg-accent-subtle' : 'border-border hover:bg-fill-hover',
                    )}
                  >
                    <span
                      aria-hidden
                      className={clsx(
                        'flex size-8 shrink-0 items-center justify-center rounded-lg border',
                        on ? 'border-transparent bg-surface text-accent-icon' : 'border-border bg-surface-subtle text-fg-muted',
                      )}
                    >
                      <Icon className="size-4" />
                    </span>
                    <span className="min-w-0 flex-1">
                      <span id={labelId} className="block text-sm font-medium text-fg">
                        {label}
                      </span>
                      <span id={`${labelId}-text`} className="block text-small text-fg-secondary">
                        {effect}
                      </span>
                    </span>
                    <input
                      type="checkbox"
                      checked={on}
                      onChange={(event) => onToggle(tool, event.target.checked)}
                      aria-labelledby={labelId}
                      aria-describedby={`${labelId}-text`}
                      className="mt-0.5 size-4 shrink-0"
                    />
                  </label>
                );
              })}
            </div>
          </div>
        );
      })}
    </div>
  );
}
