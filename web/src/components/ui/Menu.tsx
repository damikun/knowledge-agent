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
  label: string;
  items: readonly MenuItem[];
  trigger?: (props: MenuTriggerProps) => ReactNode;
  busy?: boolean;
  align?: 'start' | 'end';
  className?: string;
}

const GAP = 6;
const MARGIN = 8;

export function Menu({ label, items, trigger, busy = false, align = 'end', className }: MenuProps) {
  const [open, setOpen] = useState(false);
  const [activeIndex, setActiveIndex] = useState<number | null>(null);
  const itemsRef = useRef<Array<HTMLElement | null>>([]);

  const { refs, floatingStyles, context, placement } = useFloating<HTMLButtonElement>({
    open,
    onOpenChange: (next, _event, reason) => (next ? setOpen(true) : close(reason !== 'focus-out')),
    placement: align === 'end' ? 'bottom-end' : 'bottom-start',
    strategy: 'fixed',
    transform: false,
    middleware: [offset(GAP), flip({ padding: MARGIN }), shift({ padding: MARGIN })],
    whileElementsMounted: autoUpdate,
  });

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
      {trigger ? (
        trigger(triggerProps)
      ) : (
        <Button {...triggerProps} variant="ghost" size="icon-sm" icon={Ellipsis} loading={busy} aria-label={label} />
      )}
      {open ? (
        <FloatingPortal preserveTabOrder={false}>
          <FloatingFocusManager context={context} modal={false} returnFocus={false} guards={false}>
            <div
              ref={refs.setFloating}
              style={{ ...floatingStyles, transformOrigin }}
              className={clsx(
                'z-50 max-w-[calc(100vw-1rem)] min-w-48 animate-pop-in rounded-xl border border-border bg-raised p-1 text-sm text-fg shadow-menu outline-none',
                className,
              )}
              {...getFloatingProps({
                onKeyDown: (event) => {
                  if (event.key === 'Escape') {
                    event.stopPropagation();
                    close(true);
                  } else if (event.key === 'Tab') {
                    close(true);
                  }
                },
              })}
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
