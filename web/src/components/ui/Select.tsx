import {
  autoUpdate,
  flip,
  FloatingFocusManager,
  FloatingPortal,
  offset,
  shift,
  size as fitToViewport,
  useClick,
  useDismiss,
  useFloating,
  useInteractions,
  useListNavigation,
  useRole,
  useTypeahead,
} from '@floating-ui/react';
import { clsx } from 'clsx';
import { Check, ChevronDown, type LucideIcon } from 'lucide-react';
import { useRef, useState, type ReactNode } from 'react';
import { transformOriginOf } from './floating';

export interface SelectOption<T extends string> {
  value: T;
  /** Also what typing a letter matches. */
  label: string;
  hint?: ReactNode;
  disabled?: boolean;
}

export interface SelectProps<T extends string> {
  value: T;
  onChange: (value: T) => void;
  options: readonly SelectOption<T>[];
  'aria-label'?: string;
  'aria-describedby'?: string;
  'aria-invalid'?: boolean;
  id?: string;
  /** Submits the value with a form, like a native select. */
  name?: string;
  placeholder?: string;
  /** 32 px like inputs, or 28 px beside small buttons. */
  size?: 'sm' | 'md';
  icon?: LucideIcon;
  disabled?: boolean;
  align?: 'start' | 'end';
  /** Sets the width of the trigger, such as w-40; it fills its container otherwise, like an input. */
  className?: string;
}

const GAP = 4;
const MARGIN = 8;
const MAX_HEIGHT = 320;

/** A select-only combobox (WAI-ARIA APG) with the listbox in a portal. Typing jumps to a match, also while closed. */
export function Select<T extends string>({
  value,
  onChange,
  options,
  'aria-label': ariaLabel,
  'aria-describedby': describedBy,
  'aria-invalid': invalid,
  id,
  name,
  placeholder = 'Select',
  size = 'md',
  icon: Icon,
  disabled = false,
  align = 'start',
  className,
}: SelectProps<T>) {
  const [open, setOpen] = useState(false);
  const [activeIndex, setActiveIndex] = useState<number | null>(null);
  const listRef = useRef<Array<HTMLElement | null>>([]);
  const labelsRef = useRef<Array<string | null>>([]);
  labelsRef.current = options.map((option) => (option.disabled ? null : option.label));
  // Space chooses an option, except while typing a label that contains one.
  const typingRef = useRef(false);

  const index = options.findIndex((option) => option.value === value);
  const selectedIndex = index >= 0 ? index : null;
  const selected = selectedIndex !== null ? options[selectedIndex] : undefined;

  const { refs, floatingStyles, context, placement } = useFloating<HTMLButtonElement>({
    open,
    onOpenChange: setOpen,
    placement: align === 'end' ? 'bottom-end' : 'bottom-start',
    strategy: 'fixed',
    // Top and left instead of a transform, so the opening scale starts at the trigger.
    transform: false,
    middleware: [
      offset(GAP),
      flip({ padding: MARGIN }),
      shift({ padding: MARGIN }),
      fitToViewport({
        padding: MARGIN,
        apply({ rects, elements, availableHeight }) {
          Object.assign(elements.floating.style, {
            maxHeight: `${Math.min(MAX_HEIGHT, availableHeight)}px`,
            minWidth: `${rects.reference.width}px`,
          });
        },
      }),
    ],
    whileElementsMounted: autoUpdate,
  });

  const choose = (next: number) => {
    const option = options[next];
    if (option && !option.disabled && option.value !== value) {
      onChange(option.value);
    }
    setOpen(false);
  };

  const { getReferenceProps, getFloatingProps, getItemProps } = useInteractions([
    // Opens on press, so a press, drag and release on an option chooses it as in a native select.
    useClick(context, { event: 'mousedown' }),
    useDismiss(context),
    useRole(context, { role: 'select' }),
    useListNavigation(context, {
      listRef,
      activeIndex,
      selectedIndex,
      onNavigate: setActiveIndex,
      loop: true,
      disabledIndices: options.flatMap((option, i) => (option.disabled ? [i] : [])),
    }),
    useTypeahead(context, {
      listRef: labelsRef,
      activeIndex,
      selectedIndex,
      onMatch: (match) => (open ? setActiveIndex(match) : choose(match)),
      onTypingChange: (typing) => {
        typingRef.current = typing;
      },
    }),
  ]);

  const transformOrigin = transformOriginOf(placement);
  // The listbox needs a name too; a visible <label> names the trigger, so it lends its text.
  const listLabel = ariaLabel ?? refs.domReference.current?.labels?.[0]?.textContent ?? undefined;

  return (
    <>
      <button
        ref={refs.setReference}
        type="button"
        id={id}
        disabled={disabled}
        aria-label={ariaLabel}
        aria-describedby={describedBy}
        aria-invalid={invalid || undefined}
        className={clsx('field flex items-center gap-2 text-left', size === 'sm' && 'h-7 py-0 text-small', className)}
        {...getReferenceProps()}
      >
        {Icon ? <Icon className="size-4 shrink-0 text-fg-muted" aria-hidden /> : null}
        <span className={clsx('min-w-0 flex-1 truncate', !selected && 'text-fg-muted')}>{selected?.label ?? placeholder}</span>
        <ChevronDown className="-mr-0.5 size-4 shrink-0 text-fg-muted" aria-hidden />
      </button>
      {name ? <input type="hidden" name={name} value={value} /> : null}
      {open ? (
        <FloatingPortal>
          <FloatingFocusManager context={context} modal={false}>
            <div
              ref={refs.setFloating}
              aria-label={listLabel}
              style={{ ...floatingStyles, transformOrigin }}
              className="z-50 max-w-[calc(100vw-1rem)] animate-pop-in overflow-y-auto rounded-xl border border-border bg-raised p-1 text-sm text-fg shadow-menu outline-none"
              {...getFloatingProps({
                // React passes events up through portals, so Esc would also close a sheet around the trigger.
                onKeyDown: (event) => {
                  if (event.key === 'Escape') {
                    event.stopPropagation();
                    setOpen(false);
                  }
                },
              })}
            >
              {options.map((option, i) => {
                const active = i === activeIndex;
                const isSelected = i === selectedIndex;
                return (
                  <div
                    key={option.value}
                    ref={(node) => {
                      listRef.current[i] = node;
                    }}
                    tabIndex={active ? 0 : -1}
                    aria-disabled={option.disabled || undefined}
                    data-active={active || undefined}
                    className={clsx(
                      // The fill marks the active option for the mouse and the keyboard alike, so it needs no ring.
                      'flex h-8 cursor-default items-center gap-2 rounded-md px-2 outline-none select-none',
                      option.disabled ? 'text-fg-disabled' : 'text-fg data-active:bg-fill-hover',
                      'forced-colors:data-active:outline-2 forced-colors:data-active:outline-[Highlight]',
                    )}
                    {...getItemProps({
                      active,
                      selected: isSelected,
                      onClick: () => choose(i),
                      onKeyDown: (event) => {
                        if (event.key === 'Enter' || (event.key === ' ' && !typingRef.current)) {
                          event.preventDefault();
                          choose(i);
                        }
                      },
                    })}
                  >
                    <span className="min-w-0 flex-1 truncate">{option.label}</span>
                    {option.hint != null ? <span className="shrink-0 text-xs text-fg-muted tabular-nums">{option.hint}</span> : null}
                    <Check className={clsx('size-4 shrink-0', isSelected ? 'text-fg' : 'invisible')} aria-hidden />
                  </div>
                );
              })}
            </div>
          </FloatingFocusManager>
        </FloatingPortal>
      ) : null}
    </>
  );
}
