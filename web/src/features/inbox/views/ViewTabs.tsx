import { clsx } from 'clsx';
import { ChevronDown } from 'lucide-react';
import { useCallback, useLayoutEffect, useRef, useState, type KeyboardEvent } from 'react';
import { Link, useNavigate, type To } from 'react-router';
import { Menu } from '../../../components/ui/Menu';
import { formatNumber } from '../../../lib/format';
import { useKeptReturnState } from '../../../lib/returnTo';
import { INBOX_VIEWS, type InboxViewDefinition } from '../inboxState';
import type { InboxCounts, InboxState, InboxView } from '../types';

export interface ViewTabsProps {
  state: InboxState;
  counts: InboxCounts | undefined;
  linkTo: (view: InboxView) => To;
  panelId: string;
  /** Set for deep links: no tab is current and no count claims the list. */
  context?: boolean;
}

const GAP = 4;
const SEPARATOR = 17;
const MORE = 96;

/** Mine shows its unread count, the others their total. */
function pillOf(
  view: InboxView,
  counts: InboxCounts | undefined,
): { value: number; tone: 'urgent' | 'unread' | 'plain' | 'zero'; label: string } | null {
  if (!counts) {
    return null;
  }
  if (view === 'mine') {
    const unread = counts.unread.mine;
    return { value: unread, tone: unread > 0 ? 'unread' : 'zero', label: `${formatNumber(unread)} unread` };
  }
  const value = counts.views[view];
  return { value, tone: value === 0 ? 'zero' : view === 'waiting' ? 'urgent' : 'plain', label: formatNumber(value) };
}

function TabContent({ view, active, counts }: { view: InboxViewDefinition; active: boolean; counts: InboxCounts | undefined }) {
  const Icon = view.icon;
  const pill = pillOf(view.id, counts);
  const unread = view.group === 'primary' && view.id !== 'mine' ? (counts?.unread[view.id] ?? 0) : 0;
  return (
    <>
      <Icon
        strokeWidth={active ? 2 : 1.75}
        className={clsx('size-4 shrink-0', active ? 'text-accent-icon' : view.group === 'primary' ? 'text-fg-secondary' : 'text-fg-muted')}
        aria-hidden
      />
      <span>{view.label}</span>
      {pill ? (
        <span
          className={clsx(
            'inline-flex h-[18px] min-w-[18px] items-center justify-center rounded-full px-1.5 text-xs tabular-nums',
            pill.tone === 'urgent' && 'bg-warning-subtle font-semibold text-warning-text',
            pill.tone === 'unread' && 'bg-accent-subtle font-semibold text-accent-text',
            pill.tone === 'plain' && 'bg-fill-muted text-fg-secondary',
            pill.tone === 'zero' && 'font-normal text-fg-muted',
          )}
        >
          {pill.label === formatNumber(pill.value) ? (
            pill.label
          ) : (
            <>
              <span aria-hidden>{formatNumber(pill.value)}</span>
              <span className="sr-only">{pill.label}</span>
            </>
          )}
        </span>
      ) : null}
      {unread > 0 ? (
        <>
          <span className="size-1.5 shrink-0 rounded-full bg-accent" aria-hidden />
          <span className="sr-only">, {formatNumber(unread)} unread</span>
        </>
      ) : null}
    </>
  );
}

function tabClass(view: InboxViewDefinition, active: boolean) {
  const primary = view.group === 'primary';
  return clsx(
    'relative flex shrink-0 items-center gap-1.5 rounded-md px-2 whitespace-nowrap outline-offset-[-2px]',
    primary ? 'text-sm font-medium' : 'text-small',
    active ? 'text-fg' : primary ? 'text-fg-secondary hover:text-fg' : 'text-fg-muted hover:text-fg',
    active && 'after:absolute after:inset-x-2 after:bottom-0 after:h-0.5 after:rounded-full after:bg-fg',
  );
}

/** How many tabs fit, in order; all of them when unmeasured (e.g. in tests). */
export function tabsThatFit(widths: readonly number[], available: number | null): number {
  if (available === null || widths.length === 0 || widths.some((width) => width === 0)) {
    return INBOX_VIEWS.length;
  }
  const extent = (count: number) =>
    INBOX_VIEWS.slice(0, count).reduce(
      (sum, view, index) => sum + widths[index]! + (index > 0 ? GAP : 0) + (view.group === 'secondary' && INBOX_VIEWS[index - 1]?.group === 'primary' ? SEPARATOR : 0),
      0,
    );
  if (extent(INBOX_VIEWS.length) <= available) {
    return INBOX_VIEWS.length;
  }
  let count = INBOX_VIEWS.length - 1;
  while (count > 1 && extent(count) + GAP + MORE > available) {
    count -= 1;
  }
  return count;
}

/** Tabs that don't fit move into a More menu from the end, so labels are never truncated. */
export function ViewTabs({ state, counts: allCounts, linkTo, panelId, context = false }: ViewTabsProps) {
  const counts = context ? undefined : allCounts;
  const current = context ? null : state.view;
  const navigate = useNavigate();
  const kept = useKeptReturnState();
  const barRef = useRef<HTMLDivElement>(null);
  const measureRefs = useRef<Array<HTMLSpanElement | null>>([]);
  const [available, setAvailable] = useState<number | null>(null);
  const [widths, setWidths] = useState<number[]>([]);

  const measure = useCallback(() => {
    const next = measureRefs.current.map((element) => element?.offsetWidth ?? 0);
    setWidths((old) => (old.length === next.length && old.every((width, index) => width === next[index]) ? old : next));
  }, []);

  useLayoutEffect(() => {
    const bar = barRef.current;
    if (!bar || typeof ResizeObserver === 'undefined') {
      return undefined;
    }
    const observer = new ResizeObserver(([entry]) => {
      setAvailable(entry && entry.contentRect.width > 0 ? entry.contentRect.width : null);
      // Again once the web font loads, since it widens the labels.
      measure();
    });
    observer.observe(bar);
    return () => observer.disconnect();
  }, [measure]);

  // The hidden copies change width with the counts.
  useLayoutEffect(measure, [counts, measure]);

  const shown = tabsThatFit(widths, available);
  const visible = INBOX_VIEWS.slice(0, shown);
  const hidden = INBOX_VIEWS.slice(shown);
  const activeHidden = hidden.find((view) => view.id === current);

  const onKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    const tabs = [...event.currentTarget.querySelectorAll<HTMLElement>('[role="tab"]')];
    const index = tabs.indexOf(document.activeElement as HTMLElement);
    if (index < 0) {
      return;
    }
    const next =
      event.key === 'ArrowRight' ? (index + 1) % tabs.length : event.key === 'ArrowLeft' ? (index - 1 + tabs.length) % tabs.length : event.key === 'Home' ? 0 : event.key === 'End' ? tabs.length - 1 : -1;
    if (next >= 0) {
      event.preventDefault();
      tabs[next]?.focus();
    }
  };

  return (
    <nav aria-label="Inbox views" className="relative flex h-12 shrink-0 items-stretch gap-2 border-b border-border-subtle px-3">
      <div ref={barRef} className="flex min-w-0 flex-1 items-stretch">
        <div role="tablist" aria-label="Views" onKeyDown={onKeyDown} className="flex min-w-0 items-stretch gap-1">
          {visible.map((view, index) => {
            const active = current === view.id;
            return (
              <div key={view.id} role="presentation" className="flex items-stretch gap-1">
                {view.group === 'secondary' && visible[index - 1]?.group === 'primary' ? (
                  <span role="presentation" className="mx-2 my-3.5 w-px shrink-0 bg-border-subtle" />
                ) : null}
                <Link
                  role="tab"
                  to={linkTo(view.id)}
                  state={kept}
                  aria-selected={active}
                  aria-controls={panelId}
                  tabIndex={active || (!visible.some((candidate) => candidate.id === current) && index === 0) ? 0 : -1}
                  className={tabClass(view, active)}
                >
                  <TabContent view={view} active={active} counts={counts} />
                </Link>
              </div>
            );
          })}
        </div>
        {hidden.length > 0 ? (
          <div className="ml-1 flex shrink-0 items-center">
            <Menu
              label="More views"
              align="end"
              items={hidden.map((view) => {
                const pill = pillOf(view.id, counts);
                return {
                  label: pill ? `${view.label} (${pill.label})` : view.label,
                  icon: view.icon,
                  onSelect: () => void navigate(linkTo(view.id), { state: kept }),
                };
              })}
              trigger={(props) => (
                <button
                  {...props}
                  type="button"
                  className={clsx(
                    'relative flex h-8 items-center gap-1 rounded-md px-2 text-small whitespace-nowrap hover:bg-fill-hover',
                    activeHidden ? 'font-medium text-fg' : 'text-fg-muted hover:text-fg',
                  )}
                >
                  {activeHidden ? (
                    <>
                      <activeHidden.icon strokeWidth={2} className="size-4 text-accent-icon" aria-hidden />
                      {activeHidden.label}
                    </>
                  ) : (
                    'More'
                  )}
                  <ChevronDown className="size-3.5" aria-hidden />
                </button>
              )}
            />
          </div>
        ) : null}
      </div>
      {/* Invisible copies of every tab, for measuring their widths. */}
      <div aria-hidden inert className="pointer-events-none invisible absolute top-0 left-0 flex h-0 overflow-hidden">
        {INBOX_VIEWS.map((view, index) => (
          <span
            key={view.id}
            ref={(element) => {
              measureRefs.current[index] = element;
            }}
            className={tabClass(view, false)}
          >
            <TabContent view={view} active={false} counts={counts} />
          </span>
        ))}
      </div>
    </nav>
  );
}
