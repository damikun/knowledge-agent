import { useRef, type ReactNode } from 'react';
import { createPortal } from 'react-dom';
import { useFocusTrap, useScrollLock } from './focus';

export interface DrawerProps {
  open: boolean;
  onClose: () => void;
  label: string;
  children: ReactNode;
}

export function Drawer({ open, onClose, label, children }: DrawerProps) {
  const panelRef = useRef<HTMLDivElement>(null);
  useFocusTrap(panelRef, { active: open, onEscape: onClose });

  useScrollLock(open);

  if (!open) {
    return null;
  }
  return createPortal(
    <div className="fixed inset-0 z-40">
      <div className="absolute inset-0 animate-fade-in bg-overlay" aria-hidden="true" onMouseDown={onClose} />
      <div
        ref={panelRef}
        role="dialog"
        aria-modal="true"
        aria-label={label}
        tabIndex={-1}
        className="absolute inset-y-0 left-0 flex w-72 max-w-[85vw] animate-drawer-in flex-col border-r border-border bg-sidebar shadow-dialog outline-none"
      >
        {children}
      </div>
    </div>,
    document.body,
  );
}
