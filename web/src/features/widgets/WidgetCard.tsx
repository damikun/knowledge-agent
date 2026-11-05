import { ShieldAlert } from 'lucide-react';
import { useId, type ReactNode } from 'react';
import { Link } from 'react-router';
import type { EntityMeta } from '../../components/ui/EntityCard';
import type { WidgetDto } from './types';
import { WidgetThumbnail } from './WidgetThumbnail';

export interface WidgetCardProps {
  widget: WidgetDto;
  to: string;
  description?: ReactNode;
  warning?: ReactNode;
  status: ReactNode;
  actions?: ReactNode;
  meta: readonly EntityMeta[];
  /** Fires on pointer or focus, before the card is opened; used for prefetching. */
  onIntent?: () => void;
}

/** Like EntityCard: a stretched title link with the actions raised above it, so no button sits inside a link. */
export function WidgetCard({ widget, to, description, warning, status, actions, meta, onIntent }: WidgetCardProps) {
  const titleId = useId();
  return (
    <article
      aria-labelledby={titleId}
      onPointerEnter={onIntent}
      onFocus={onIntent}
      className="relative flex h-full min-w-0 flex-col overflow-hidden rounded-2xl border border-border bg-surface shadow-card hover:border-border-hover has-[[data-card-target]:focus-visible]:outline-2 has-[[data-card-target]:focus-visible]:outline-offset-2 has-[[data-card-target]:focus-visible]:outline-focus"
    >
      <WidgetThumbnail
        appearance={widget.settings.appearance}
        greeting={widget.settings.behavior.greeting}
        suggestedQuestions={widget.settings.behavior.suggestedQuestions}
        placeholder={widget.settings.behavior.placeholder}
        className="w-full shrink-0 border-b border-border-subtle"
      />
      <div className="flex flex-1 flex-col p-4">
        <div className="flex items-start justify-between gap-3">
          <div className="min-w-0">
            <h2 id={titleId} className="truncate text-sm font-semibold text-fg">
              <Link to={to} data-card-target className="focus-visible:outline-none after:absolute after:inset-0 after:rounded-2xl">
                {widget.name}
              </Link>
            </h2>
            {description ? <p className="mt-0.5 truncate text-small text-fg-secondary">{description}</p> : null}
            {warning ? (
              <p className="mt-1 flex items-center gap-1.5 text-xs font-medium text-warning-text">
                <ShieldAlert className="size-3.5 shrink-0 text-warning" aria-hidden />
                {warning}
              </p>
            ) : null}
          </div>
          <div className="relative z-10 -mt-0.5 flex shrink-0 items-center gap-1">
            {status}
            {actions}
          </div>
        </div>
        {meta.length > 0 ? (
          <ul className="mt-3 flex flex-wrap gap-x-4 gap-y-1 text-xs text-fg-muted">
            {meta.map(({ icon: Icon, label }, index) => (
              <li key={index} className="flex items-center gap-1.5">
                {Icon ? <Icon className="size-3.5 shrink-0" aria-hidden /> : null}
                {label}
              </li>
            ))}
          </ul>
        ) : null}
      </div>
    </article>
  );
}
