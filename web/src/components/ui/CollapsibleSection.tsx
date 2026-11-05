import { clsx } from 'clsx';
import { ChevronRight } from 'lucide-react';
import type { ReactNode } from 'react';

export interface CollapsibleSectionProps {
  title: ReactNode;
  description?: ReactNode;
  /** Opens or closes it from outside, such as to show a field with an error; pair it with `onOpenChange`. */
  open?: boolean;
  defaultOpen?: boolean;
  /** Also fires when find in page opens it. */
  onOpenChange?: (open: boolean) => void;
  children: ReactNode;
  className?: string;
}

/** A native <details>, so keyboards, screen readers and find in page open it without help. */
export function CollapsibleSection({ title, description, open, defaultOpen = false, onOpenChange, children, className }: CollapsibleSectionProps) {
  return (
    <details
      open={open ?? defaultOpen}
      onToggle={(event) => onOpenChange?.(event.currentTarget.open)}
      className={clsx('group border-t border-border-subtle pt-4', className)}
    >
      <summary className="flex w-fit cursor-pointer list-none items-start gap-2 rounded-md text-sm select-none [&::-webkit-details-marker]:hidden">
        <ChevronRight className="mt-0.5 size-4 shrink-0 text-fg-muted transition-transform duration-100 ease-out group-open:rotate-90" aria-hidden />
        <span className="min-w-0">
          <span className="block font-medium text-fg">{title}</span>
          {description ? <span className="mt-0.5 block text-small text-fg-muted">{description}</span> : null}
        </span>
      </summary>
      <div className="pt-4 pl-6">{children}</div>
    </details>
  );
}
