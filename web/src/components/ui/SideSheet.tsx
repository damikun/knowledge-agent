import { clsx } from 'clsx';
import { X } from 'lucide-react';
import { useId, useRef, type ReactNode, type RefObject } from 'react';
import { createPortal } from 'react-dom';
import { Button } from './Button';
import { useFocusTrap, useScrollLock } from './focus';

export interface SideSheetProps {
  open: boolean;
  onClose: () => void;
  title: ReactNode;
  description?: ReactNode;
  children: ReactNode;
  footer?: ReactNode;
  /** 480 or 560 px; full width on phones. */
  size?: 'md' | 'lg';
  /** A modal sheet dims the page and keeps focus inside; a non-modal one leaves the page usable beside it. */
  modal?: boolean;
  initialFocus?: RefObject<HTMLElement | null>;
}

const widths = {
  md: 'sm:max-w-120',
  lg: 'sm:max-w-140',
};

export function SideSheet({ open, onClose, title, description, children, footer, size = 'md', modal = true, initialFocus }: SideSheetProps) {
  const panelRef = useRef<HTMLDivElement>(null);
  const titleId = useId();
  const descriptionId = useId();

  useFocusTrap(panelRef, { active: open, onEscape: onClose, initialFocus, contain: modal });
  useScrollLock(open && modal);

  if (!open) {
    return null;
  }

  return createPortal(
    <>
      {modal ? <div className="fixed inset-0 z-40 animate-fade-in bg-overlay" aria-hidden="true" onMouseDown={onClose} /> : null}
      <div
        ref={panelRef}
        role="dialog"
        aria-modal={modal || undefined}
        aria-labelledby={titleId}
        aria-describedby={description ? descriptionId : undefined}
        tabIndex={-1}
        className={clsx(
          'fixed inset-y-0 right-0 z-40 flex w-full animate-sheet-in flex-col border-l border-border bg-raised shadow-dialog outline-none',
          widths[size],
        )}
      >
        <div className="flex items-start justify-between gap-4 border-b border-border-subtle px-6 py-4">
          <div className="min-w-0">
            <h2 id={titleId} className="truncate text-section text-fg">
              {title}
            </h2>
            {description ? (
              <p id={descriptionId} className="mt-1 text-sm text-fg-secondary">
                {description}
              </p>
            ) : null}
          </div>
          <Button variant="ghost" size="icon-sm" icon={X} aria-label="Close" onClick={onClose} className="-my-0.5 -mr-2" />
        </div>
        <div className="min-h-0 flex-1 overflow-y-auto px-6 py-5">{children}</div>
        {footer ? <div className="flex flex-wrap items-center justify-end gap-2 border-t border-border-subtle px-6 py-4">{footer}</div> : null}
      </div>
    </>,
    document.body,
  );
}
