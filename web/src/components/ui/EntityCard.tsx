import { clsx } from 'clsx';
import type { LucideIcon } from 'lucide-react';
import { useId, type AriaAttributes, type ReactNode } from 'react';
import { Link } from 'react-router';
import { Avatar } from './Avatar';

export function EntityAvatar({ name }: { name: string }) {
  return <Avatar name={name} size="lg" shape="tile" />;
}

export interface EntityMeta {
  icon?: LucideIcon;
  label: ReactNode;
}

export interface EntityCardProps extends AriaAttributes {
  /** Names the card for screen readers, unless an aria-label or aria-labelledby is given. */
  title: string;
  id?: string;
  description?: ReactNode;
  /** Where the card leads. With neither `to` nor `onOpen` the card is not clickable. */
  to?: string;
  onOpen?: () => void;
  visual?: ReactNode;
  meta?: readonly EntityMeta[];
  status?: ReactNode;
  /** Buttons that stay clickable on their own, such as Delete or a menu. */
  actions?: ReactNode;
  /** The heading level that fits the page; a card grid right under the page title takes h2. */
  titleAs?: 'h2' | 'h3';
  /** Built in and not removable: drawn on a quieter, dashed card. */
  builtIn?: boolean;
  className?: string;
}

export function EntityCard({
  title,
  id,
  description,
  to,
  onOpen,
  visual,
  meta,
  status,
  actions,
  titleAs: Title = 'h3',
  builtIn = false,
  className,
  ...aria
}: EntityCardProps) {
  const titleId = useId();
  const clickable = Boolean(to || onOpen);
  return (
    <article
      id={id}
      aria-labelledby={aria['aria-label'] === undefined ? titleId : undefined}
      {...aria}
      className={clsx(
        'relative flex min-w-0 flex-col rounded-2xl border p-4',
        builtIn ? 'border-dashed border-border-strong bg-surface-subtle' : 'border-border bg-surface shadow-card',
        clickable &&
          'hover:border-border-hover has-[[data-card-target]:focus-visible]:outline-2 has-[[data-card-target]:focus-visible]:outline-offset-2 has-[[data-card-target]:focus-visible]:outline-focus',
        className,
      )}
    >
      <div className="flex items-start justify-between gap-3">
        {visual ?? <EntityAvatar name={title} />}
        {status || actions ? (
          <div className="relative z-10 flex min-w-0 items-center gap-1">
            {status}
            {actions}
          </div>
        ) : null}
      </div>
      {/* The title link stretches over the card; the actions sit above it, so no buttons end up inside a link. */}
      <Title id={titleId} className="mt-3 truncate text-sm font-semibold text-fg">
        {to ? (
          <Link to={to} data-card-target className="focus-visible:outline-none after:absolute after:inset-0 after:rounded-2xl">
            {title}
          </Link>
        ) : onOpen ? (
          <button type="button" data-card-target onClick={onOpen} className="text-left focus-visible:outline-none after:absolute after:inset-0 after:rounded-2xl">
            {title}
          </button>
        ) : (
          title
        )}
      </Title>
      {description ? <p className="mt-0.5 truncate text-small text-fg-secondary">{description}</p> : null}
      {meta && meta.length > 0 ? (
        <ul className="mt-3 flex flex-wrap gap-x-4 gap-y-1 text-xs text-fg-muted">
          {meta.map(({ icon: Icon, label }, index) => (
            <li key={index} className="flex items-center gap-1.5">
              {Icon ? <Icon className="size-3.5 shrink-0" aria-hidden /> : null}
              {label}
            </li>
          ))}
        </ul>
      ) : null}
    </article>
  );
}
