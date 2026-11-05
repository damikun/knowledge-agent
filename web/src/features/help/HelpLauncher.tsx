import { CircleHelp } from 'lucide-react';
import { useRef, useState } from 'react';
import { useLocation } from 'react-router';
import { Button } from '../../components/ui/Button';
import { SideSheet } from '../../components/ui/SideSheet';
import { useKeydown } from '../../lib/hooks/useKeydown';
import { isAdmin, useMe } from '../auth/api';
import { useHelpStatus } from './api';
import { HelpPanel } from './HelpPanel';

/** Typing "?" in a field is text, not the shortcut. */
function isEditable(target: EventTarget | null): boolean {
  if (!(target instanceof HTMLElement)) {
    return false;
  }
  return target.isContentEditable || target.closest('input, textarea, select, [contenteditable=""], [contenteditable="true"], [role="textbox"]') !== null;
}

function isHelpShortcut(event: KeyboardEvent): boolean {
  return event.key === '?' && !event.ctrlKey && !event.metaKey && !event.altKey && !event.defaultPrevented && !isEditable(event.target);
}

export function HelpLauncher() {
  const status = useHelpStatus();
  const admin = isAdmin(useMe().data);
  const location = useLocation();
  const [open, setOpen] = useState(false);
  const inputRef = useRef<HTMLTextAreaElement>(null);
  const enabled = status.data?.enabled === true;

  // Refetch on open; setup may have changed since the last look.
  const show = () => {
    if (!open) {
      setOpen(true);
      void status.refetch();
    }
  };

  useKeydown(
    (event) => {
      if (isHelpShortcut(event)) {
        event.preventDefault();
        show();
      }
    },
    { enabled, target: 'document' },
  );

  if (!enabled || !status.data) {
    return null;
  }

  // On phones the sheet covers the page, so following a link closes it.
  const onNavigate = () => {
    if (typeof window.matchMedia === 'function' && !window.matchMedia('(min-width: 640px)').matches) {
      setOpen(false);
    }
  };

  return (
    <>
      <Button
        variant="ghost"
        size="icon"
        icon={CircleHelp}
        aria-label="Help"
        title="Help (?)"
        aria-keyshortcuts="?"
        aria-haspopup="dialog"
        aria-expanded={open}
        onClick={() => (open ? setOpen(false) : show())}
      />
      <SideSheet
        open={open}
        onClose={() => setOpen(false)}
        modal={false}
        initialFocus={inputRef}
        title="Help"
        description="Answers about this portal from its own help. It never sees your customers or their conversations."
      >
        <HelpPanel status={status.data} page={location.pathname} admin={admin} onNavigate={onNavigate} inputRef={inputRef} />
      </SideSheet>
    </>
  );
}
