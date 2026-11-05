import { clsx } from 'clsx';
import {
  AppWindow,
  Bot,
  ChartColumn,
  ClipboardCheck,
  Cpu,
  House,
  Inbox,
  Library,
  FlaskConical,
  PanelLeftClose,
  PanelLeftOpen,
  BellRing,
  ScrollText,
  ThumbsUp,
  UserCog,
  UsersRound,
  Workflow,
  Wrench,
  type LucideIcon,
} from 'lucide-react';
import { useId, type MouseEvent } from 'react';
import { NavLink } from 'react-router';
import { Button } from '../components/ui/Button';
import { useSystemInfo } from '../features/system/api';

export interface NavItem {
  to: string;
  label: string;
  icon: LucideIcon;
  /** Hidden from other roles; routes.tsx guards the pages themselves. */
  adminOnly?: boolean;
}

interface NavGroup {
  label: string;
  items: NavItem[];
  foot?: boolean;
}

const INBOX_PATH = '/inbox';
export const PRODUCT = 'Knowledge Agent';

const NAV_GROUPS: NavGroup[] = [
  {
    label: 'Support',
    items: [
      { to: '/home', label: 'Home', icon: House, adminOnly: true },
      { to: INBOX_PATH, label: 'Inbox', icon: Inbox },
      { to: '/contacts', label: 'Contacts', icon: UsersRound },
    ],
  },
  {
    label: 'Insights',
    items: [
      {
        to: '/analytics',
        label: 'Analytics',
        icon: ChartColumn,
        adminOnly: true,
      },
      { to: '/feedback', label: 'Feedback', icon: ThumbsUp },
    ],
  },
  {
    label: 'Improve',
    items: [
      {
        to: '/playground',
        label: 'Playground',
        icon: FlaskConical,
        adminOnly: true,
      },
      { to: '/agents', label: 'Agents', icon: Bot, adminOnly: true },
      { to: '/tools', label: 'Tools', icon: Wrench, adminOnly: true },
      { to: '/widgets', label: 'Widgets', icon: AppWindow, adminOnly: true },
      { to: '/sources', label: 'Sources', icon: Library, adminOnly: true },
      { to: '/prompts', label: 'Prompts', icon: ScrollText, adminOnly: true },
      {
        to: '/evals',
        label: 'Evaluations',
        icon: ClipboardCheck,
        adminOnly: true,
      },
    ],
  },
  {
    label: 'Settings',
    foot: true,
    items: [
      { to: '/notifications', label: 'Notifications', icon: BellRing, adminOnly: true },
      { to: '/models', label: 'AI models', icon: Cpu, adminOnly: true },
      { to: '/team', label: 'Team', icon: UserCog, adminOnly: true },
      { to: '/system', label: 'System', icon: Workflow, adminOnly: true },
    ],
  },
];

function visibleGroups(admin: boolean): NavGroup[] {
  return NAV_GROUPS.map((group) => ({
    ...group,
    items: group.items.filter((item) => admin || !item.adminOnly),
  })).filter((group) => group.items.length > 0);
}

// The mark and icons sit 28 px from the left in both widths, so they don't move when the rail narrows.
export function Brand({ compact = false }: { compact?: boolean }) {
  return (
    <div className="flex h-12 shrink-0 items-center gap-2.5 px-4">
      <span
        aria-hidden
        className="flex size-6 shrink-0 items-center justify-center rounded-md bg-accent text-[10px] font-semibold tracking-tight text-fg-on-accent"
      >
        KA
      </span>
      <span className={clsx('truncate text-sm font-semibold text-fg', compact && 'sr-only')}>{PRODUCT}</span>
    </div>
  );
}

function WaitingBadge({ count, compact }: { count: number; compact: boolean }) {
  return (
    <span
      className={clsx(
        'inline-flex items-center justify-center rounded-full bg-accent font-medium text-fg-on-accent tabular-nums',
        compact ? 'absolute -top-1 -right-1 h-4 min-w-4 px-1 text-[10px] ring-2 ring-sidebar' : 'ml-auto h-4.5 min-w-4.5 px-1.5 text-xs',
      )}
    >
      <span aria-hidden>{count > 99 ? '99+' : count}</span>
      <span className="sr-only">, {count} waiting</span>
    </span>
  );
}

interface NavigationProps {
  admin: boolean;
  waiting: number | undefined;
  compact?: boolean;
  onNavigate?: () => void;
}

// Chrome shows the focus ring on a clicked link at the next key press, so pointer clicks (detail > 0) blur it.
function blurAfterPointerClick(event: MouseEvent<HTMLAnchorElement>) {
  if (event.detail > 0) {
    event.currentTarget.blur();
  }
}

export function Navigation({ admin, waiting, compact = false, onNavigate }: NavigationProps) {
  const id = useId();
  return (
    <nav aria-label="Main" className="flex min-h-0 flex-1 flex-col gap-4 overflow-y-auto px-3 py-2">
      {visibleGroups(admin).map((group, index) => (
        <div
          key={group.label}
          role="group"
          aria-labelledby={`${id}-${index}`}
          className={clsx(
            'flex flex-col gap-px',
            compact && index > 0 && !group.foot && 'border-t border-border pt-3',
            group.foot && 'mt-auto border-t border-border pt-3',
          )}
        >
          <div id={`${id}-${index}`} className={compact ? 'sr-only' : 'flex h-6 items-center px-2 text-xs text-fg-muted'}>
            {group.label}
          </div>
          {group.items.map(({ to, label, icon: Icon }) => (
            <NavLink
              key={to}
              to={to}
              onClick={(event) => {
                blurAfterPointerClick(event);
                onNavigate?.();
              }}
              title={compact ? label : undefined}
              className={({ isActive }) =>
                clsx(
                  'group relative flex h-8 items-center gap-2.5 rounded-md px-2 text-sm',
                  isActive
                    ? 'bg-fill-active font-medium text-fg before:absolute before:top-1.5 before:bottom-1.5 before:-left-3 before:w-[3px] before:rounded-r-full before:bg-accent'
                    : 'text-fg-secondary hover:bg-fill-hover hover:text-fg',
                )
              }
            >
              {({ isActive }) => (
                <>
                  <Icon
                    strokeWidth={isActive ? 2 : 1.75}
                    className={clsx('size-4 shrink-0', isActive ? 'text-accent-icon' : 'text-fg-secondary group-hover:text-fg')}
                    aria-hidden
                  />
                  <span className={compact ? 'sr-only' : 'truncate'}>{label}</span>
                  {to === INBOX_PATH && waiting ? <WaitingBadge count={waiting} compact={compact} /> : null}
                </>
              )}
            </NavLink>
          ))}
        </div>
      ))}
    </nav>
  );
}

function useVersion(): string | null {
  const info = useSystemInfo();
  return info.data ? `v${info.data.version} · ${info.data.environment}` : null;
}

export function VersionLabel() {
  const version = useVersion();
  return version ? <span className="min-w-0 truncate px-2 text-xs text-fg-muted tabular-nums">{version}</span> : null;
}

interface MenuToggleProps {
  admin: boolean;
  collapsed: boolean;
  onToggle: () => void;
  /** Id of the sidebar, for aria-controls. */
  controls: string;
}

export function MenuToggle({ admin, collapsed, onToggle, controls }: MenuToggleProps) {
  return (
    <div className={clsx('flex h-12 shrink-0 items-center gap-2 px-3', collapsed && 'justify-center')}>
      {admin && !collapsed ? <VersionLabel /> : null}
      <Button
        variant="ghost"
        size="icon-sm"
        icon={collapsed ? PanelLeftOpen : PanelLeftClose}
        aria-label={collapsed ? 'Expand menu' : 'Collapse menu'}
        aria-expanded={!collapsed}
        aria-controls={controls}
        className={collapsed ? undefined : 'ml-auto'}
        onClick={onToggle}
      />
    </div>
  );
}

export function findSection(pathname: string): NavItem | undefined {
  return NAV_GROUPS.flatMap((group) => group.items).find((item) => pathname === item.to || pathname.startsWith(`${item.to}/`));
}
