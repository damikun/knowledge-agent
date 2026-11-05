import { Plus, X } from 'lucide-react';
import { useId, useMemo, useState, type FormEvent } from 'react';
import { Button } from '../../components/ui/Button';
import { Popover } from '../../components/ui/Popover';
import { Select, type SelectOption } from '../../components/ui/Select';
import { useAttributeValues } from './api';
import { describeCondition, OP_LABELS, TYPE_LABELS, VALUELESS_OPS } from './filters';
import type { ContactCondition, FilterAttribute } from './types';

export interface FilterBuilderProps {
  /** Index of the condition, conditions.length for a new one, or null while closed. */
  editing: number | null;
  onEditingChange: (editing: number | null) => void;
  conditions: readonly ContactCondition[];
  onChange: (conditions: ContactCondition[]) => void;
  attributes: readonly FilterAttribute[];
  widgets: readonly { id: string; name: string }[];
}

interface Draft {
  attr: string;
  op: string;
  value: string;
  to: string;
}

function draftOf(condition: ContactCondition | undefined): Draft {
  if (!condition) {
    return { attr: '', op: '', value: '', to: '' };
  }
  const value = condition.value;
  return {
    attr: condition.attr,
    op: condition.op,
    value: Array.isArray(value) ? String(value[0]) : value === undefined ? '' : String(value),
    to: Array.isArray(value) ? String(value[1]) : '',
  };
}

function conditionOf(draft: Draft, attribute: FilterAttribute): ContactCondition | null {
  if (!draft.op) {
    return null;
  }
  if (VALUELESS_OPS.has(draft.op)) {
    return { attr: draft.attr, op: draft.op };
  }
  if (draft.op === 'between') {
    const from = Number(draft.value);
    const to = Number(draft.to);
    return draft.value !== '' && draft.to !== '' && Number.isFinite(from) && Number.isFinite(to) && from <= to ? { attr: draft.attr, op: draft.op, value: [from, to] } : null;
  }
  const text = draft.value.trim();
  if (!text) {
    return null;
  }
  if (draft.op === 'within_days' || attribute.type === 'number') {
    const number = Number(text);
    return Number.isFinite(number) ? { attr: draft.attr, op: draft.op, value: number } : null;
  }
  return { attr: draft.attr, op: draft.op, value: text };
}

function ConditionEditor({
  initial,
  attributes,
  widgets,
  onApply,
  onCancel,
}: {
  initial: ContactCondition | undefined;
  attributes: readonly FilterAttribute[];
  widgets: readonly { id: string; name: string }[];
  onApply: (condition: ContactCondition) => void;
  onCancel: () => void;
}) {
  const id = useId();
  const [draft, setDraft] = useState<Draft>(() => draftOf(initial));
  const [search, setSearch] = useState('');
  const attribute = attributes.find((item) => item.key === draft.attr);
  const suggestions = useAttributeValues(draft.attr, attribute?.type === 'string' && !attribute.builtIn);

  if (!attribute) {
    const term = search.trim().toLowerCase();
    const matching = attributes.filter((item) => !term || item.label.toLowerCase().includes(term) || item.key.includes(term));
    const groups = [
      { name: 'Built-in', items: matching.filter((item) => item.builtIn) },
      { name: 'Custom', items: matching.filter((item) => !item.builtIn) },
    ].filter((group) => group.items.length > 0);
    return (
      <div className="w-72 space-y-2 p-2">
        <input
          type="search"
          aria-label="Find an attribute"
          placeholder="Find an attribute"
          className="field"
          value={search}
          autoFocus
          onChange={(event) => setSearch(event.target.value)}
        />
        <div className="max-h-72 space-y-2 overflow-y-auto">
          {groups.length === 0 ? <p className="px-2 py-1 text-small text-fg-muted">No attribute matches.</p> : null}
          {groups.map((group) => (
            <div key={group.name} role="group" aria-labelledby={`${id}-${group.name}`}>
              <p id={`${id}-${group.name}`} className="px-2 py-1 text-xs font-medium text-fg-muted">
                {group.name}
              </p>
              <ul>
                {group.items.map((item) => (
                  <li key={item.key}>
                    <button
                      type="button"
                      className="flex h-8 w-full items-center gap-2 rounded-md px-2 text-left text-small text-fg hover:bg-fill-hover"
                      onClick={() => setDraft({ attr: item.key, op: item.ops[0] ?? '', value: '', to: '' })}
                    >
                      <span className="min-w-0 flex-1 truncate">{item.label}</span>
                      <span className="text-xs text-fg-muted">{TYPE_LABELS[item.type]}</span>
                    </button>
                  </li>
                ))}
              </ul>
            </div>
          ))}
        </div>
      </div>
    );
  }

  const condition = conditionOf(draft, attribute);
  const opOptions: SelectOption<string>[] = attribute.ops.map((op) => ({ value: op, label: OP_LABELS[op] ?? op }));
  const submit = (event: FormEvent) => {
    event.preventDefault();
    if (condition) {
      onApply(condition);
    }
  };
  const inputType = attribute.type === 'number' || draft.op === 'within_days' ? 'number' : attribute.type === 'date' ? 'date' : 'text';

  return (
    <form className="w-72 space-y-3 p-3" onSubmit={submit} aria-label={`Filter by ${attribute.label}`}>
      <p className="text-small font-medium text-fg">{attribute.label}</p>
      <Select aria-label="Condition" size="sm" value={draft.op} options={opOptions} onChange={(op) => setDraft({ ...draft, op })} />
      {VALUELESS_OPS.has(draft.op) ? null : attribute.type === 'widget' ? (
        <Select
          aria-label="Widget"
          size="sm"
          placeholder="Choose a widget"
          value={draft.value}
          options={widgets.map((widget) => ({ value: widget.id, label: widget.name }))}
          onChange={(value) => setDraft({ ...draft, value })}
        />
      ) : draft.op === 'between' ? (
        <div className="flex items-center gap-2">
          <input aria-label="From" type="number" className="field h-7 text-small" value={draft.value} onChange={(event) => setDraft({ ...draft, value: event.target.value })} />
          <span className="text-small text-fg-muted">and</span>
          <input aria-label="To" type="number" className="field h-7 text-small" value={draft.to} onChange={(event) => setDraft({ ...draft, to: event.target.value })} />
        </div>
      ) : (
        <div className="space-y-1">
          <div className="flex items-center gap-2">
            <input
              aria-label="Value"
              type={inputType}
              min={draft.op === 'within_days' ? 1 : undefined}
              className="field h-7 text-small"
              value={draft.value}
              autoFocus
              list={suggestions.data && suggestions.data.length > 0 ? `${id}-values` : undefined}
              onChange={(event) => setDraft({ ...draft, value: event.target.value })}
            />
            {draft.op === 'within_days' ? <span className="text-small text-fg-muted">days</span> : null}
          </div>
          {suggestions.data && suggestions.data.length > 0 ? (
            <datalist id={`${id}-values`}>
              {suggestions.data.map((value) => (
                <option key={value} value={value} />
              ))}
            </datalist>
          ) : null}
        </div>
      )}
      <div className="flex justify-end gap-2">
        <Button size="sm" variant="ghost" onClick={onCancel}>
          Cancel
        </Button>
        <Button size="sm" type="submit" disabled={!condition}>
          Apply
        </Button>
      </div>
    </form>
  );
}

export function FilterBuilder({ editing, onEditingChange: setEditing, conditions, onChange, attributes, widgets }: FilterBuilderProps) {
  // Anchored to the clicked chip, or to Filter for a new condition.
  const [addButton, setAddButton] = useState<HTMLButtonElement | null>(null);
  const [chip, setChip] = useState<HTMLElement | null>(null);
  const byKey = useMemo(() => new Map(attributes.map((item) => [item.key, item])), [attributes]);
  const widgetName = (widgetId: string) => widgets.find((widget) => widget.id.toLowerCase() === widgetId.toLowerCase())?.name ?? 'a removed widget';

  const anchor = editing !== null && editing < conditions.length ? chip : addButton;
  const close = () => setEditing(null);

  return (
    <div className="flex flex-wrap items-center gap-2" role="group" aria-label="Filters">
      {conditions.map((condition, index) => {
        const text = describeCondition(condition, byKey.get(condition.attr), widgetName);
        return (
          <span key={`${condition.attr}-${index}`} className="inline-flex h-7 items-center rounded-md border border-border-strong bg-surface text-small text-fg">
            <button
              type="button"
              className="h-full rounded-l-md px-2.5 hover:bg-fill-hover"
              aria-label={`Edit filter: ${text}`}
              onClick={(event) => {
                setChip(event.currentTarget);
                setEditing(index);
              }}
            >
              {text}
            </button>
            <Button
              size="icon-sm"
              variant="ghost"
              icon={X}
              aria-label={`Remove filter: ${text}`}
              onClick={() => onChange(conditions.filter((_, other) => other !== index))}
            />
          </span>
        );
      })}
      <Button ref={setAddButton} size="sm" icon={Plus} onClick={() => setEditing(conditions.length)} aria-expanded={editing !== null}>
        Filter
      </Button>
      {conditions.length > 1 ? (
        <Button size="sm" variant="ghost" onClick={() => onChange([])}>
          Clear all
        </Button>
      ) : null}
      <Popover open={editing !== null} onClose={close} anchor={anchor} label="Filter contacts">
        {editing !== null ? (
          <ConditionEditor
            key={editing}
            initial={conditions[editing]}
            attributes={attributes}
            widgets={widgets}
            onCancel={close}
            onApply={(condition) => {
              const next = [...conditions];
              next[editing] = condition;
              onChange(next);
              close();
            }}
          />
        ) : null}
      </Popover>
    </div>
  );
}
