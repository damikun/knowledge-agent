import { autoUpdate, flip, FloatingFocusManager, FloatingPortal, offset, shift, useDismiss, useFloating, useInteractions } from '@floating-ui/react';
import { clsx } from 'clsx';
import { useLayoutEffect, type ReactNode, type RefObject } from 'react';
import { transformOriginOf } from './floating';

export interface PopoverProps {
  open: boolean;
  onClose: () => void;
  /** Pass `anchor` instead when the opener changes, such as one of several chips. */
  anchorRef?: RefObject<HTMLElement | null>;
  anchor?: HTMLElement | null;
  children: ReactNode;
  label: string;
  align?: 'start' | 'end';
  className?: string;
  id?: string;
  initialFocus?: RefObject<HTMLElement | null>;
}

const GAP = 6;
const MARGIN = 8;

/** Non-modal, in a portal so overflow containers never clip it. */
export function Popover({ open, onClose, anchorRef, anchor, children, label, align = 'start', className, id, initialFocus }: PopoverProps) {
  const { refs, floatingStyles, context, placement } = useFloating({
    open,
    onOpenChange: (next) => {
      if (!next) {
        onClose();
      }
    },
    placement: align === 'end' ? 'bottom-end' : 'bottom-start',
    strategy: 'fixed',
    // Top and left instead of a transform, so the opening scale centers on the popover rather than the viewport corner.
    transform: false,
    middleware: [offset(GAP), flip({ padding: MARGIN }), shift({ padding: MARGIN })],
    whileElementsMounted: autoUpdate,
  });

  useLayoutEffect(() => {
    refs.setReference(anchor !== undefined ? anchor : (anchorRef?.current ?? null));
  }, [refs, anchorRef, anchor]);

  const dismiss = useDismiss(context);
  const { getFloatingProps } = useInteractions([dismiss]);

  if (!open) {
    return null;
  }

  const transformOrigin = transformOriginOf(placement);

  return (
    <FloatingPortal>
      <FloatingFocusManager context={context} modal={false} initialFocus={initialFocus ?? 0}>
        <div
          ref={refs.setFloating}
          id={id}
          role="dialog"
          aria-label={label}
          tabIndex={-1}
          style={{ ...floatingStyles, transformOrigin }}
          className={clsx(
            'z-50 max-w-[calc(100vw-1rem)] animate-pop-in rounded-xl border border-border bg-raised text-sm text-fg shadow-menu outline-none',
            className,
          )}
          {...getFloatingProps({
            // React passes events up through portals, so Esc would also reach the editor around the anchor.
            onKeyDown: (event) => {
              if (event.key === 'Escape') {
                event.stopPropagation();
                onClose();
              }
            },
          })}
        >
          {children}
        </div>
      </FloatingFocusManager>
    </FloatingPortal>
  );
}
