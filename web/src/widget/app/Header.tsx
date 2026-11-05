import { EllipsisVertical, X } from 'lucide-react';
import { useCallback, useId, useRef, useState, type ComponentProps, type KeyboardEvent } from 'react';
import { useWidget } from './context';
import { Avatar } from './Messages';

export interface MenuItem {
  key: string;
  label: string;
  onSelect: () => void;
}

function HeaderButton(props: Omit<ComponentProps<'button'>, 'type' | 'className'>) {
  return (
    <button
      type="button"
      {...props}
      className="grid size-8 shrink-0 place-items-center rounded-full text-header-ink transition-colors hover:bg-raised focus-visible:outline-header-ink aria-expanded:bg-raised"
    />
  );
}

function HeaderMenu({ items }: { items: MenuItem[] }) {
  const { t, mark } = useWidget();
  const [open, setOpen] = useState(false);
  // Focus returns here when the menu closes.
  const buttonRef = useRef<HTMLButtonElement>(null);
  const menuId = useId();

  // Focus the first item; a press anywhere else closes the menu.
  const menuRef = useCallback((menu: HTMLDivElement) => {
    menu.querySelector<HTMLElement>('[role="menuitem"]')?.focus();
    const onPointerDown = (event: PointerEvent) => {
      const target = event.target as Node;
      if (!menu.contains(target) && !buttonRef.current?.contains(target)) {
        setOpen(false);
      }
    };
    document.addEventListener('pointerdown', onPointerDown);
    return () => document.removeEventListener('pointerdown', onPointerDown);
  }, []);

  const close = (refocus: boolean) => {
    setOpen(false);
    if (refocus) {
      buttonRef.current?.focus();
    }
  };

  const onKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    const entries = [...event.currentTarget.querySelectorAll<HTMLElement>('[role="menuitem"]')];
    const index = entries.indexOf(document.activeElement as HTMLElement);
    const focusAt = (next: number) => entries[(next + entries.length) % entries.length]?.focus();
    switch (event.key) {
      case 'ArrowDown':
        event.preventDefault();
        focusAt(index + 1);
        break;
      case 'ArrowUp':
        event.preventDefault();
        focusAt(index - 1);
        break;
      case 'Home':
        event.preventDefault();
        focusAt(0);
        break;
      case 'End':
        event.preventDefault();
        focusAt(entries.length - 1);
        break;
      case 'Escape':
        // Closes the menu only, not the whole chat.
        event.preventDefault();
        event.stopPropagation();
        close(true);
        break;
      case 'Tab':
        close(false);
        break;
      default:
        break;
    }
  };

  return (
    <div className="relative">
      <HeaderButton
        ref={buttonRef}
        {...mark('header.menu')}
        aria-label={t('menu')}
        aria-haspopup="menu"
        aria-expanded={open}
        aria-controls={open ? menuId : undefined}
        onClick={() => setOpen((value) => !value)}
      >
        <EllipsisVertical className="size-4.5" aria-hidden="true" />
      </HeaderButton>
      {open ? (
        <div
          ref={menuRef}
          id={menuId}
          role="menu"
          aria-label={t('menu')}
          onKeyDown={onKeyDown}
          className="absolute top-full right-0 z-20 mt-1.5 min-w-44 origin-top-right rounded-2xl border border-line bg-panel p-1 text-ink shadow-[0_16px_40px_-12px_rgb(0_0_0/0.35)] motion-safe:animate-rise"
        >
          {items.map((item) => (
            <button
              key={item.key}
              type="button"
              role="menuitem"
              tabIndex={-1}
              onClick={() => {
                close(true);
                item.onSelect();
              }}
              className="block w-full rounded-xl px-3 py-2 text-left text-sm hover:bg-raised focus:bg-raised focus-visible:outline-offset-[-2px]"
            >
              {item.label}
            </button>
          ))}
        </div>
      ) : null}
    </div>
  );
}

export function Header({ menuItems, onClose }: { menuItems: MenuItem[]; onClose: () => void }) {
  const { config, t, mark } = useWidget();
  return (
    <header {...mark('appearance.header')} className="flex items-center gap-3 bg-header py-3 pr-2.5 pl-4 text-header-ink">
      <span {...mark('appearance.avatar')} className="shrink-0">
        <Avatar size="md" name={config.title} imageUrl={config.avatarUrl} inHeader />
      </span>
      <div className="min-w-0 flex-1">
        <h1 {...mark('appearance.title')} className="truncate text-[15px] leading-tight font-semibold tracking-[-0.01em]">
          {config.title}
        </h1>
        {config.subtitle ? (
          <p {...mark('appearance.subtitle')} className="mt-0.5 truncate text-[12.5px] leading-tight">
            {config.subtitle}
          </p>
        ) : null}
      </div>
      {menuItems.length > 0 ? <HeaderMenu items={menuItems} /> : null}
      <HeaderButton {...mark('header.close')} aria-label={t('close')} onClick={onClose}>
        <X className="size-4.5" aria-hidden="true" />
      </HeaderButton>
    </header>
  );
}
