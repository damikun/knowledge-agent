import { clsx } from 'clsx';
import type { LucideIcon } from 'lucide-react';
import type { ReactNode } from 'react';

export interface ChoiceCardGroupProps {
  label: string;
  errorId?: string;
  children: ReactNode;
}

export function ChoiceCardGroup({ label, errorId, children }: ChoiceCardGroupProps) {
  return (
    <div
      role="radiogroup"
      aria-label={label}
      aria-describedby={errorId}
      aria-invalid={errorId ? true : undefined}
      className="grid grid-cols-[repeat(auto-fill,minmax(min(100%,15rem),1fr))] gap-4"
    >
      {children}
    </div>
  );
}

export interface ChoiceCardProps {
  name: string;
  checked: boolean;
  /** Without it the card is the only, fixed choice. */
  onChoose?: () => void;
  icon: LucideIcon;
  title: ReactNode;
  /** Names the radio by the title, for cards whose description would otherwise read as the name. */
  titleId?: string;
  description?: ReactNode;
  descriptionId: string;
  children?: ReactNode;
}

export function ChoiceCard({ name, checked, onChoose, icon: Icon, title, titleId, description, descriptionId, children }: ChoiceCardProps) {
  return (
    <label
      className={clsx(
        'flex h-full cursor-pointer flex-col rounded-2xl border bg-surface p-4 shadow-card has-[:focus-visible]:outline-2 has-[:focus-visible]:outline-offset-2 has-[:focus-visible]:outline-focus',
        checked ? 'border-accent-edge bg-accent-subtle' : 'border-border hover:border-border-hover',
      )}
    >
      <input
        type="radio"
        name={name}
        checked={checked}
        onChange={onChoose}
        readOnly={!onChoose}
        aria-labelledby={titleId}
        aria-describedby={description ? descriptionId : undefined}
        className="sr-only"
      />
      <span aria-hidden className="flex size-10 items-center justify-center rounded-xl border border-border bg-surface-subtle text-fg-secondary">
        <Icon className="size-5" />
      </span>
      <span id={titleId} className="mt-3 text-sm font-semibold text-fg">
        {title}
      </span>
      {description ? (
        <span id={descriptionId} className="mt-1 text-small text-fg-secondary">
          {description}
        </span>
      ) : null}
      {children}
    </label>
  );
}
