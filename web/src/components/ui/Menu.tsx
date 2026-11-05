import {
  autoUpdate,
  flip,
  FloatingFocusManager,
  FloatingPortal,
  offset,
  shift,
  useClick,
  useDismiss,
  useFloating,
  useInteractions,
  useListNavigation,
  useRole,
} from '@floating-ui/react';
import { clsx } from 'clsx';
import { Ellipsis, type LucideIcon } from 'lucide-react';
import { useRef, useState, type ComponentProps, type ReactNode } from 'react';
import { transformOriginOf } from './floating';
import { Button } from './Button';

export interface MenuItem {
  label: ReactNode;
  icon?: LucideIcon;
  onSelect: () => void;
  danger?: boolean;
}

export type MenuTriggerProps = ComponentProps<'button'>;

export interface MenuProps {
  /** Names the menu, and the default trigger, such as "Actions for “Handbook”". */
  label: string;
  items: readonly MenuItem[];
  /** The button that opens the menu. Without it, a ghost "more" icon button named by `label`. */
  trigger?: (props: MenuTriggerProps) => ReactNode;
  busy?: boolean;
  align?: 'start' | 'end';
  /** Sets the width, such as w-80; the menu is at least 192 px wide and otherwise fits its items. */
  className?: string;
}

const GAP = 6;
const MARGIN = 8;

/** A WAI-ARIA menu, rendered in a portal next to its trigger. */
export function Menu({ label, items, trigger, busy = false, align = 'end', className }: MenuProps) {
  const [open, setOpen] = useState(false);
  const [activeIndex, setActiveIndex] = useState<number | null>(null);
  const itemsRef = useRef<Array<HTMLElement | null>>([]);

  const { refs, floatingStyles, context, placement } = useFloating<HTMLButtonElement>({
    open,
    // Tab has already moved focus on; every other way of closing returns it to the trigger.
    onOpenChange: (next, _event, reason) => (next ? setOpen(true) : close(reason !== 'focus-out')),
    placement: align === 'end' ? 'bottom-end' : 'bottom-start',
    strategy: 'fixed',
    // Top and left instead of a transform, so the opening scale centers on the menu rather than the viewport corner.
    transform: false,
    middleware: [offset(GAP), flip({ padding: MARGIN }), shift({ padding: MARGIN })],
    whileElementsMounted: autoUpdate,
  });

  // Floating UI restores focus a microtask after unmount and would pull it back out of a dialog an item opened,
  // so the menu restores focus itself, before the item's action runs.
  const close = (returnFocus: boolean) => {
    setOpen(false);
    if (returnFocus) {
      refs.domReference.current?.focus({ preventScroll: true });
    }
  };

  const { getReferenceProps, getFloatingProps, getItemProps } = useInteractions([
    useClick(context),
    useRole(context, { role: 'menu' }),
    useDismiss(context),
    useListNavigation(context, { listRef: itemsRef, activeIndex, onNavigate: setActiveIndex, loop: true }),
  ]);

  const choose = (item: MenuItem) => {
    close(true);
    item.onSelect();
  };

  const triggerProps = { ref: refs.setReference, ...getReferenceProps() } as MenuTriggerProps;
  const transformOrigin = transformOriginOf(placement);

  return (
    <>
      {trigger ? trigger(triggerProps) : <Button {...triggerProps} variant="ghost" size="icon-sm" icon={Ellipsis} loading={busy} aria-label={label} />}
      {open ? (
        <FloatingPortal>
          <FloatingFocusManager context={context} modal={false} returnFocus={false}>
            <div
              ref={refs.setFloating}
              style={{ ...floatingStyles, transformOrigin }}
              className={clsx(
                'z-50 max-w-[calc(100vw-1rem)] min-w-48 animate-pop-in rounded-xl border border-border bg-raised p-1 text-sm text-fg shadow-menu outline-none',
                className,
              )}
              {...getFloatingProps({
                // React passes events up through portals, so Esc would also reach whatever surrounds the trigger.
                onKeyDown: (event) => {
                  if (event.key === 'Escape') {
                    event.stopPropagation();
                    close(true);
                  }
                },
              })}
              // Named by `label` rather than by the trigger, whose text may be a count such as "2 to fix".
              aria-labelledby={undefined}
              aria-label={label}
            >
              {items.map((item, index) => {
                const Icon = item.icon;
                return (
                  <button
                    key={index}
                    ref={(node) => {
                      itemsRef.current[index] = node;
                    }}
                    type="button"
                    role="menuitem"
                    tabIndex={activeIndex === index ? 0 : -1}
                    className={clsx(
                      // The item under the pointer takes focus, so one fill marks it for the mouse and the keyboard, as in Select.
                      'flex min-h-8 w-full items-center gap-2 rounded-md px-2 py-1.5 text-left outline-none forced-colors:focus:outline-2 forced-colors:focus:outline-[Highlight]',
                      item.danger ? 'text-danger-text focus:bg-danger-subtle' : 'text-fg focus:bg-fill-hover',
                    )}
                    {...getItemProps({ onClick: () => choose(item) })}
                  >
                    {Icon ? <Icon className={clsx('size-4 shrink-0', item.danger ? 'text-danger' : 'text-fg-muted')} aria-hidden /> : null}
                    <span className="min-w-0 flex-1">{item.label}</span>
                  </button>
                );
              })}
            </div>
          </FloatingFocusManager>
        </FloatingPortal>
      ) : null}
    </>
  );
}
