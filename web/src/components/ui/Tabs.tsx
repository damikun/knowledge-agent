import { clsx } from 'clsx';
import type { LucideIcon } from 'lucide-react';
import { useRef, type KeyboardEvent, type ReactNode } from 'react';

export interface TabItem<T extends string> {
  id: T;
  label: string;
  icon?: LucideIcon;
}

function tabId(prefix: string, id: string): string {
  return `${prefix}-tab-${id}`;
}

function tabPanelId(prefix: string, id: string): string {
  return `${prefix}-panel-${id}`;
}

export interface TabsProps<T extends string> {
  idPrefix: string;
  items: readonly TabItem<T>[];
  value: T;
  onChange: (value: T) => void;
  label: string;
  className?: string;
}

/** WAI-ARIA tabs with automatic activation; render the panels with TabPanel. */
export function Tabs<T extends string>({ idPrefix, items, value, onChange, label, className }: TabsProps<T>) {
  const refs = useRef<Array<HTMLButtonElement | null>>([]);

  const onKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    const index = items.findIndex((item) => item.id === value);
    let next = index;
    if (event.key === 'ArrowRight') {
      next = (index + 1) % items.length;
    } else if (event.key === 'ArrowLeft') {
      next = (index - 1 + items.length) % items.length;
    } else if (event.key === 'Home') {
      next = 0;
    } else if (event.key === 'End') {
      next = items.length - 1;
    } else {
      return;
    }
    event.preventDefault();
    const item = items[next];
    if (item) {
      onChange(item.id);
      refs.current[next]?.focus();
    }
  };

  return (
    <div
      role="tablist"
      aria-label={label}
      onKeyDown={onKeyDown}
      className={clsx('flex h-10 items-stretch gap-4 border-b border-border-subtle', className)}
    >
      {items.map((item, index) => {
        const selected = item.id === value;
        const Icon = item.icon;
        return (
          <button
            key={item.id}
            ref={(el) => {
              refs.current[index] = el;
            }}
            type="button"
            role="tab"
            id={tabId(idPrefix, item.id)}
            aria-selected={selected}
            aria-controls={tabPanelId(idPrefix, item.id)}
            tabIndex={selected ? 0 : -1}
            onClick={() => onChange(item.id)}
            // The bar overlaps the row's bottom border; as a border it stays visible in forced-color modes.
            className={clsx(
              'relative inline-flex items-center gap-1.5 px-1 text-sm font-medium whitespace-nowrap',
              selected
                ? 'text-fg after:absolute after:inset-x-0 after:-bottom-px after:border-b-2 after:border-fg forced-colors:after:border-[Highlight]'
                : 'text-fg-secondary hover:text-fg',
            )}
          >
            {Icon ? <Icon className="size-4" aria-hidden /> : null}
            {item.label}
          </button>
        );
      })}
    </div>
  );
}

export function TabPanel({ idPrefix, id, children, className }: { idPrefix: string; id: string; children: ReactNode; className?: string }) {
  return (
    <div role="tabpanel" id={tabPanelId(idPrefix, id)} aria-labelledby={tabId(idPrefix, id)} tabIndex={0} className={clsx('rounded-lg', className)}>
      {children}
    </div>
  );
}
