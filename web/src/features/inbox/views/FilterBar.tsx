import { Check, Plus, Tags } from 'lucide-react';
import { useRef, useState, type ReactNode } from 'react';
import { Button } from '../../../components/ui/Button';
import { Menu, type MenuTriggerProps } from '../../../components/ui/Menu';
import { Popover } from '../../../components/ui/Popover';
import { mergeRefs } from '../../../lib/mergeRefs';
import type { Label } from '../../labels/types';
import { PersonFilter, usePersonName } from '../../people/PersonFilter';
import { useSegments, useStaff } from '../api';
import { withoutFilters } from '../inboxState';
import { widgetLabel } from '../status';
import type { InboxState } from '../types';
import { ActiveFilterChip } from './ActiveFilterChip';

type FilterKind = 'channel' | 'widget' | 'label' | 'contact' | 'assignee' | 'rating' | 'thumbs' | 'period' | 'segment' | 'handover';
export type Choices<T extends string> = ReadonlyArray<readonly [T, string]>;

const KIND_NAMES: Readonly<Record<FilterKind, string>> = {
  channel: 'Channel',
  widget: 'Widget',
  label: 'Label',
  contact: 'Contact',
  assignee: 'Assignee',
  rating: 'Rating',
  thumbs: 'Thumbs down',
  period: 'Started',
  segment: 'Segment',
  handover: 'Asked for a person',
};

// Order of the + Filter menu. "Asked for a person" only appears as a chip from legacy links.
const MENU_KINDS: readonly FilterKind[] = ['channel', 'widget', 'label', 'contact', 'assignee', 'rating', 'thumbs', 'period', 'segment'];

const CHANNELS: Choices<'widget' | 'mcp' | 'api' | 'portal'> = [
  ['widget', 'Website'],
  ['mcp', 'AI apps (MCP)'],
  ['api', 'HTTP API'],
  ['portal', 'Playground tests'],
];

const RATINGS: Choices<'good' | 'poor'> = [
  ['good', '4 or 5 stars'],
  ['poor', '1 or 2 stars'],
];

const RATING_CHIPS: Choices<'good' | 'poor'> = [
  ['good', '4-5'],
  ['poor', '1-2'],
];

const PERIODS: Choices<'7' | '30' | '90'> = [
  ['7', 'Last 7 days'],
  ['30', 'Last 30 days'],
  ['90', 'Last 90 days'],
];

export function textOf<T extends string>(choices: Choices<T>, value: string): string {
  return choices.find(([choice]) => choice === value)?.[1] ?? value;
}

function OptionList<T extends string>({
  label,
  choices,
  value,
  onPick,
}: {
  label: string;
  choices: Choices<T>;
  value: string;
  onPick: (value: T) => void;
}) {
  return (
    <ul aria-label={label} className="max-h-72 space-y-0.5 overflow-y-auto">
      {choices.map(([choice, text]) => (
        <li key={choice}>
          <button
            type="button"
            aria-pressed={choice === value}
            onClick={() => onPick(choice)}
            className="flex w-full min-w-0 items-center gap-2 rounded-md px-2 py-1.5 text-left text-small text-fg hover:bg-fill-hover"
          >
            <span className="min-w-0 flex-1 truncate">{text}</span>
            {choice === value ? <Check className="size-4 shrink-0 text-accent-icon" aria-hidden /> : null}
          </button>
        </li>
      ))}
    </ul>
  );
}

export interface FilterBarProps {
  state: InboxState;
  onChange: (next: InboxState) => void;
  /** By lower-case id; undefined for support users, who can't read widgets. */
  widgetNames: ReadonlyMap<string, string> | undefined;
  canFilterByWidget: boolean;
  labels: readonly Label[];
  onManageLabels?: () => void;
}

export function FilterBar({ state, onChange, widgetNames, canFilterByWidget, labels, onManageLabels }: FilterBarProps) {
  const [editing, setEditing] = useState<FilterKind | null>(null);
  // The chip or + Filter that opened the value picker, for anchoring.
  const [anchor, setAnchor] = useState<HTMLElement | null>(null);
  const addRef = useRef<HTMLButtonElement | null>(null);
  const contactName = usePersonName(state.contactId);
  const needsStaff = editing === 'assignee' || (state.assignee !== '' && state.assignee !== 'me' && state.assignee !== 'none');
  const staff = useStaff(needsStaff);
  const segments = useSegments();
  const segmentChoices: Choices<string> = (segments.data ?? []).map((segment) => [segment.id, segment.name] as const);

  const widgetChoices: Choices<string> = [...(widgetNames ?? new Map<string, string>())].map(([id, name]) => [id, name] as const).sort((a, b) => a[1].localeCompare(b[1]));
  const labelChoices: Choices<string> = labels.map((label) => [label.id.toLowerCase(), label.name] as const);
  const assigneeChoices: Choices<string> = [['me', 'Me'], ['none', 'Nobody'], ...(staff.data ?? []).map((member) => [member.id, member.name] as const)];
  const assigneeText =
    state.assignee === 'me' ? 'Me' : state.assignee === 'none' ? 'Nobody' : (staff.data?.find((member) => member.id === state.assignee)?.name ?? 'Someone');

  const chips: Array<{ kind: FilterKind; value: string; clear: Partial<InboxState> }> = [
    ...(state.channel ? [{ kind: 'channel' as const, value: textOf(CHANNELS, state.channel), clear: { channel: '' as const } }] : []),
    ...(state.widgetId ? [{ kind: 'widget' as const, value: widgetLabel(state.widgetId, widgetNames), clear: { widgetId: '' } }] : []),
    ...(state.labelId ? [{ kind: 'label' as const, value: labelChoices.find(([id]) => id === state.labelId)?.[1] ?? 'Deleted label', clear: { labelId: '' } }] : []),
    ...(state.contactId ? [{ kind: 'contact' as const, value: contactName, clear: { contactId: '' } }] : []),
    ...(state.assignee ? [{ kind: 'assignee' as const, value: assigneeText, clear: { assignee: '' } }] : []),
    ...(state.rating ? [{ kind: 'rating' as const, value: textOf(RATING_CHIPS, state.rating), clear: { rating: '' as const } }] : []),
    ...(state.thumbsDown ? [{ kind: 'thumbs' as const, value: 'Yes', clear: { thumbsDown: false } }] : []),
    ...(state.period ? [{ kind: 'period' as const, value: textOf(PERIODS, state.period), clear: { period: '' as const } }] : []),
    ...(state.segmentId
      ? [{ kind: 'segment' as const, value: segmentChoices.find(([id]) => id === state.segmentId)?.[1] ?? (segments.isPending ? 'Loading…' : 'Deleted segment'), clear: { segmentId: '' } }]
      : []),
    ...(state.handover ? [{ kind: 'handover' as const, value: state.handover === 'yes' ? 'Yes' : 'No', clear: { handover: '' as const } }] : []),
  ];
  const available = MENU_KINDS.filter(
    (kind) =>
      !chips.some((chip) => chip.kind === kind) &&
      (kind !== 'widget' || (canFilterByWidget && widgetChoices.length > 0)) &&
      (kind !== 'label' || labels.length > 0 || Boolean(onManageLabels)) &&
      (kind !== 'segment' || segmentChoices.length > 0),
  );

  const pick = (patch: Partial<InboxState>) => {
    onChange({ ...state, ...patch });
    setEditing(null);
  };

  const open = (kind: FilterKind, opener: HTMLElement | null) => {
    if (kind === 'thumbs') {
      onChange({ ...state, thumbsDown: true });
      return;
    }
    setAnchor(opener);
    setEditing(kind);
  };

  let editor: ReactNode = null;
  switch (editing) {
    case 'channel':
      editor = <OptionList label="Channel" choices={CHANNELS} value={state.channel} onPick={(channel) => pick({ channel })} />;
      break;
    case 'widget':
      editor = <OptionList label="Widget" choices={widgetChoices} value={state.widgetId} onPick={(widgetId) => pick({ widgetId })} />;
      break;
    case 'label':
      editor = (
        <div className="space-y-2">
          {labelChoices.length > 0 ? (
            <OptionList label="Label" choices={labelChoices} value={state.labelId} onPick={(labelId) => pick({ labelId })} />
          ) : (
            <p className="px-2 text-small text-fg-muted">No labels yet.</p>
          )}
          {onManageLabels ? (
            <Button
              size="sm"
              variant="ghost"
              icon={Tags}
              onClick={() => {
                setEditing(null);
                onManageLabels();
              }}
            >
              Manage labels
            </Button>
          ) : null}
        </div>
      );
      break;
    case 'contact':
      editor = <PersonFilter value={state.contactId} onChange={(contactId) => pick({ contactId })} />;
      break;
    case 'assignee':
      editor = <OptionList label="Assignee" choices={assigneeChoices} value={state.assignee} onPick={(assignee) => pick({ assignee })} />;
      break;
    case 'rating':
      editor = <OptionList label="Rating" choices={RATINGS} value={state.rating} onPick={(rating) => pick({ rating })} />;
      break;
    case 'period':
      editor = <OptionList label="Started" choices={PERIODS} value={state.period} onPick={(period) => pick({ period })} />;
      break;
    case 'segment':
      editor = <OptionList label="Segment" choices={segmentChoices} value={state.segmentId} onPick={(segmentId) => pick({ segmentId })} />;
      break;
    default:
      editor = null;
  }

  const addTrigger = ({ ref, ...props }: MenuTriggerProps) => (
    <button
      {...props}
      ref={mergeRefs(ref, addRef)}
      type="button"
      aria-label="Add filter"
      className="inline-flex h-6 items-center gap-1 rounded-full px-2 text-xs font-medium text-fg-secondary hover:bg-fill-hover hover:text-fg"
    >
      <Plus className="size-3.5" aria-hidden />
      Filter
    </button>
  );

  return (
    <div className="flex flex-wrap items-center gap-1.5">
      {chips.length > 0 ? (
        <ul aria-label="Active filters" className="flex max-w-full flex-wrap items-center gap-1.5">
          {chips.map((chip) => {
            const text = `${KIND_NAMES[chip.kind]}: ${chip.value}`;
            const editable = chip.kind !== 'thumbs' && chip.kind !== 'handover';
            return (
              <ActiveFilterChip
                key={chip.kind}
                text={text}
                onEdit={editable ? (anchor) => open(chip.kind, anchor) : undefined}
                editing={editable ? editing === chip.kind : undefined}
                onRemove={() => onChange({ ...state, ...chip.clear })}
              >
                <span className="opacity-75">{KIND_NAMES[chip.kind]}: </span>
                {chip.value}
              </ActiveFilterChip>
            );
          })}
        </ul>
      ) : null}
      {available.length > 0 ? (
        <Menu
          label="Add a filter"
          align="start"
          trigger={addTrigger}
          items={available.map((kind) => ({
            label: KIND_NAMES[kind],
            // Deferred until the menu has closed and restored focus, so the picker keeps it.
            onSelect: () => window.setTimeout(() => open(kind, addRef.current), 0),
          }))}
        />
      ) : null}
      {chips.length >= 2 ? (
        <button
          type="button"
          // The pin toggle isn't a chip, so Clear leaves it.
          onClick={() => onChange({ ...withoutFilters(state), pinned: state.pinned })}
          className="inline-flex h-6 items-center rounded-full px-2 text-xs font-medium text-fg-muted hover:bg-fill-hover hover:text-fg"
        >
          Clear
        </button>
      ) : null}
      {/* Mounted per picker so it anchors to the element that opened it. */}
      {editing ? (
        <Popover key={editing} open onClose={() => setEditing(null)} anchor={anchor} label={KIND_NAMES[editing]} className="w-72 p-2">
          {editor}
        </Popover>
      ) : null}
    </div>
  );
}
