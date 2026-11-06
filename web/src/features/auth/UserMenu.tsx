import { LogOut, Monitor, Moon, Sun } from 'lucide-react';
import { useRef, useState } from 'react';
import { Avatar } from '../../components/ui/Avatar';
import { Badge } from '../../components/ui/Badge';
import { Popover } from '../../components/ui/Popover';
import { SegmentedControl } from '../../components/ui/SegmentedControl';
import { useTheme, type ThemePreference } from '../../lib/theme';
import { isAdmin, roleLabel, signOut, useMe } from './api';

const THEMES = [
  { value: 'light', label: 'Light', icon: Sun },
  { value: 'dark', label: 'Dark', icon: Moon },
  { value: 'system', label: 'System', icon: Monitor },
] as const satisfies readonly { value: ThemePreference; label: string; icon: typeof Sun }[];

export function UserMenu() {
  const me = useMe();
  const [open, setOpen] = useState(false);
  const buttonRef = useRef<HTMLButtonElement>(null);
  const theme = useTheme();

  const user = me.data;
  if (!user) {
    return null;
  }

  return (
    <>
      <button
        ref={buttonRef}
        type="button"
        onClick={() => setOpen((value) => !value)}
        aria-expanded={open}
        aria-haspopup="dialog"
        className="group rounded-full"
      >
        <Avatar name={user.name} size="sm" className="group-hover:bg-fill-active group-hover:text-fg" />
        {/* Text rather than aria-label: the initials are a picture of the person, not a label to match (2.5.3). */}
        <span className="sr-only">Account: {user.name}</span>
      </button>
      <Popover open={open} onClose={() => setOpen(false)} anchorRef={buttonRef} label="Account" align="end" className="w-72 p-1">
        <div className="flex items-start gap-3 p-3">
          <Avatar name={user.name} size="lg" />
          <div className="min-w-0 flex-1">
            <div className="truncate font-medium text-fg" title={user.name}>
              {user.name}
            </div>
            {user.email ? (
              <div className="truncate text-xs text-fg-muted" title={user.email}>
                {user.email}
              </div>
            ) : null}
            <Badge tone={isAdmin(user) ? 'accent' : 'neutral'} className="mt-2">
              {roleLabel(user)}
            </Badge>
          </div>
        </div>
        <div className="flex items-center justify-between gap-3 border-t border-border-subtle py-1.5 pr-1 pl-2">
          <span className="text-sm text-fg-secondary">Theme</span>
          <SegmentedControl label="Theme" options={THEMES} value={theme.preference} onChange={theme.setPreference} iconOnly />
        </div>
        <div className="border-t border-border-subtle pt-1">
          {user.mode === 'none' ? (
            <p className="px-2 py-2 text-xs text-fg-muted">
              <span className="block text-sm font-medium text-fg">Sign-in is off</span>
              Every request runs as a local admin in this development setup.
            </p>
          ) : (
            <button
              type="button"
              onClick={signOut}
              className="flex h-8 w-full items-center gap-2 rounded-md px-2 text-sm text-fg-secondary hover:bg-fill-hover hover:text-fg"
            >
              <LogOut className="size-4" aria-hidden />
              Sign out
            </button>
          )}
        </div>
      </Popover>
    </>
  );
}
