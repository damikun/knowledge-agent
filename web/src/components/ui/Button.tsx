import { clsx } from 'clsx';
import { LoaderCircle, type LucideIcon } from 'lucide-react';
import type { ComponentProps } from 'react';

// primary: at most one per view (a dialog counts as its own view).
// danger: confirms a deletion inside a dialog. danger-ghost: an in-page delete that opens that dialog.
export type ButtonVariant = 'primary' | 'secondary' | 'ghost' | 'danger' | 'danger-ghost';
export type ButtonSize = 'sm' | 'md' | 'lg' | 'icon' | 'icon-sm';

// Every variant has a border (transparent if unseen) so sizes match and forced-colors mode still outlines it.
const variants: Record<ButtonVariant, string> = {
  primary: 'border-transparent bg-accent text-fg-on-accent shadow-control hover:bg-accent-hover active:bg-accent-active',
  secondary: 'border-border bg-surface text-fg shadow-control hover:border-border-hover hover:bg-fill-hover',
  ghost: 'border-transparent text-fg-secondary hover:bg-fill-hover hover:text-fg',
  danger: 'border-transparent bg-danger text-fg-on-solid hover:bg-danger-hover',
  'danger-ghost': 'border-transparent text-danger-text hover:bg-danger-subtle',
};

// A disabled ghost button stays transparent, so an icon button does not turn into a gray box.
const disabledVariants: Record<ButtonVariant, string> = {
  primary: 'border-transparent bg-fill-muted text-fg-disabled',
  secondary: 'border-transparent bg-fill-muted text-fg-disabled',
  ghost: 'border-transparent text-fg-disabled',
  danger: 'border-transparent bg-fill-muted text-fg-disabled',
  'danger-ghost': 'border-transparent text-fg-disabled',
};

// 28, 32 and 36 px high, above the 24 px minimum target size of WCAG 2.2 (2.5.8).
const sizes: Record<ButtonSize, string> = {
  sm: 'h-7 gap-1.5 px-2.5 text-small',
  md: 'h-8 gap-1.5 px-3 text-sm',
  lg: 'h-9 gap-2 px-3.5 text-sm',
  icon: 'size-8',
  'icon-sm': 'size-7',
};

const iconSizes: Record<ButtonSize, string> = {
  sm: 'size-3.5',
  md: 'size-4',
  lg: 'size-4',
  icon: 'size-4',
  'icon-sm': 'size-4',
};

/** The look of a button, for links that act as one. */
export function buttonClass({ variant = 'secondary', size = 'md', disabled = false }: { variant?: ButtonVariant; size?: ButtonSize; disabled?: boolean } = {}): string {
  return clsx(
    'inline-flex shrink-0 items-center justify-center rounded-md border font-medium whitespace-nowrap transition-colors duration-100 ease-out select-none',
    // A marker without styles, so tests can count the primary actions of a view, links styled as buttons included.
    variant === 'primary' && 'action-primary',
    disabled ? disabledVariants[variant] : variants[variant],
    sizes[size],
  );
}

export interface ButtonProps extends ComponentProps<'button'> {
  variant?: ButtonVariant;
  size?: ButtonSize;
  icon?: LucideIcon;
  /** Shows a spinner instead of the icon and disables the button, keeping its colors. */
  loading?: boolean;
}

export function Button({
  variant = 'secondary',
  size = 'md',
  icon: Icon,
  loading = false,
  disabled = false,
  className,
  children,
  type = 'button',
  ...props
}: ButtonProps) {
  return (
    <button
      type={type}
      disabled={disabled || loading}
      aria-busy={loading || undefined}
      data-variant={variant}
      // Icon-only buttons show their name on hover too, so every control says what it does.
      title={props.title ?? (children == null ? props['aria-label'] : undefined)}
      className={clsx(buttonClass({ variant, size, disabled }), 'disabled:pointer-events-none', className)}
      {...props}
    >
      {loading ? (
        <LoaderCircle className={clsx(iconSizes[size], 'animate-spin')} aria-hidden />
      ) : Icon ? (
        <Icon className={iconSizes[size]} aria-hidden />
      ) : null}
      {children}
    </button>
  );
}
