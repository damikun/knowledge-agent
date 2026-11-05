import { ArrowLeft, Bot, Ellipsis, Info, PanelLeft, Pencil, Pin, PinOff, Tag, Trash2 } from 'lucide-react';
import { useId, useRef, useState, type ReactNode } from 'react';
import { Badge } from '../../components/ui/Badge';
import { Button } from '../../components/ui/Button';
import { Menu, type MenuItem } from '../../components/ui/Menu';
import { Popover } from '../../components/ui/Popover';
import { formatDateTime } from '../../lib/format';
import { mergeRefs } from '../../lib/mergeRefs';
import { useDisclosure } from '../../lib/hooks/useDisclosure';
import { useLabels } from './api';
import { LabelChip, LabelPicker } from './LabelPicker';
import { TestBadge } from './playground';
import { TitleInput } from './TitleInput';
import type { ConversationDto, ConversationListItem } from './types';
import type { ConversationActions } from './useConversationActions';

const HEADER_LABELS = 2;

/** "orchestrator-system v3 (langfuse)", or null for a conversation without a stored prompt. */
export function promptLabel(conversation: Pick<ConversationDto, 'promptName' | 'promptVersion' | 'promptOrigin'> | undefined): string | null {
  if (!conversation?.promptName) {
    return null;
  }
  const version = conversation.promptVersion != null ? ` v${conversation.promptVersion}` : '';
  const origin = conversation.promptOrigin ? ` (${conversation.promptOrigin})` : '';
  return `${conversation.promptName}${version}${origin}`;
}

export function ConversationDetailsButton({ conversation, agentName, scope }: { conversation: ConversationDto; agentName: string | null; scope: string }) {
  const details = useDisclosure();
  const ref = useRef<HTMLButtonElement>(null);
  const id = useId();
  const rows: Array<[string, string | null, boolean?]> = [
    ['Agent', agentName],
    ['Prompt', promptLabel(conversation), true],
    ['Profile', conversation.profile, true],
    ['Knowledge', scope],
    ['Started', formatDateTime(conversation.createdAt)],
    ['ID', conversation.id, true],
  ];
  return (
    <>
      <Button
        ref={ref}
        variant="ghost"
        size="icon-sm"
        icon={Info}
        aria-label="Conversation details"
        aria-expanded={details.open}
        aria-haspopup="dialog"
        aria-controls={details.open ? id : undefined}
        title="Conversation details"
        onClick={details.toggle}
      />
      <Popover id={id} open={details.open} onClose={details.close} anchorRef={ref} label="Conversation details" align="end" className="w-80 p-3">
        <dl className="divide-y divide-border-subtle text-xs">
          {rows
            .filter(([, value]) => value)
            .map(([label, value, mono]) => (
              <div key={label} className="grid grid-cols-[5.5rem_1fr] gap-2 py-1">
                <dt className="text-fg-muted">{label}</dt>
                <dd className={mono ? 'min-w-0 font-mono break-all text-fg' : 'min-w-0 break-words text-fg'}>{value}</dd>
              </div>
            ))}
        </dl>
      </Popover>
    </>
  );
}

export interface ChatHeaderProps {
  title: string;
  /** The conversation as the list has it; without it there is nothing to rename, pin, label or delete yet. */
  item: ConversationListItem | undefined;
  agentName: string | null;
  actions: ConversationActions;
  list?: { open: boolean; show: () => void };
  onBack?: () => void;
  tools: ReactNode;
}

/** Keyed by the conversation, so a rename or the label picker never carries over. */
export function ChatHeader({ title, item, agentName, actions, list, onBack, tools }: ChatHeaderProps) {
  const labels = useLabels();
  const [renaming, setRenaming] = useState(false);
  const labelPicker = useDisclosure();
  const menuButtonRef = useRef<HTMLButtonElement | null>(null);

  const menuItems: MenuItem[] = item
    ? [
        { label: 'Rename', icon: Pencil, onSelect: () => setRenaming(true) },
        { label: item.pinned ? 'Unpin' : 'Pin', icon: item.pinned ? PinOff : Pin, onSelect: () => actions.togglePin(item) },
        ...(labels.data?.available ? [{ label: 'Labels', icon: Tag, onSelect: labelPicker.show }] : []),
        { label: 'Delete', icon: Trash2, danger: true, onSelect: () => actions.remove(item) },
      ]
    : [];

  return (
    <div className="flex h-12 shrink-0 items-center gap-2 border-b border-border-subtle px-4">
      {list ? <Button variant="ghost" size="icon-sm" icon={PanelLeft} aria-label="Show conversations" aria-expanded={list.open} onClick={list.show} /> : null}
      {onBack ? <Button variant="ghost" size="icon-sm" icon={ArrowLeft} aria-label="Back" title="Back to a new test" onClick={onBack} /> : null}
      <TestBadge />
      {renaming && item ? (
        <TitleInput
          title={item.title}
          className="min-w-0 flex-1 font-semibold"
          onDone={(next) => {
            setRenaming(false);
            if (next !== null) {
              actions.rename(item, next);
            }
          }}
        />
      ) : (
        <h2 className="min-w-0 truncate text-sm font-semibold text-fg" title={title}>
          {title}
        </h2>
      )}
      {agentName ? (
        <Badge tone="neutral" className="hidden min-w-0 shrink sm:inline-flex" title="Agent">
          <Bot className="size-3 shrink-0" aria-hidden />
          <span className="sr-only">Agent: </span>
          <span className="truncate">{agentName}</span>
        </Badge>
      ) : null}
      {item && item.labels.length > 0 ? (
        <span className="hidden min-w-0 items-center gap-1 lg:flex">
          {item.labels.slice(0, HEADER_LABELS).map((label) => (
            <LabelChip key={label.id} label={label} />
          ))}
          {item.labels.length > HEADER_LABELS ? <span className="text-xs text-fg-muted">+{item.labels.length - HEADER_LABELS}</span> : null}
        </span>
      ) : null}
      <div className="ml-auto flex shrink-0 items-center gap-1">
        {tools}
        {item ? (
          <Menu
            label="Conversation actions"
            items={menuItems}
            trigger={(props) => (
              <Button
                {...props}
                ref={mergeRefs(props.ref, menuButtonRef)}
                variant="ghost"
                size="icon-sm"
                icon={Ellipsis}
                aria-label="Conversation actions"
                title="Rename, pin, label or delete"
              />
            )}
          />
        ) : null}
      </div>
      {item && labelPicker.open ? (
        <LabelPicker
          open
          onClose={labelPicker.close}
          anchorRef={menuButtonRef}
          labels={labels.data?.labels ?? []}
          value={item.labels}
          onChange={(next) => actions.setLabels(item, next)}
        />
      ) : null}
    </div>
  );
}
