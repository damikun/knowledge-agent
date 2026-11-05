import { clsx } from 'clsx';
import { LABEL_STYLES } from './labelStyles';
import type { ConversationLabel, LabelColor } from './types';

export interface LabelChipProps {
  name: string;
  color: LabelColor;
  /** Auto labels announce it to screen readers and on hover. */
  source?: ConversationLabel['source'];
  className?: string;
}

export function LabelChip({ name, color, source, className }: LabelChipProps) {
  const style = LABEL_STYLES[color];
  const auto = source === 'auto';
  return (
    <span
      data-color={color}
      title={auto ? `${name}, added automatically` : name}
      className={clsx(
        'inline-flex h-5 max-w-40 min-w-0 items-center gap-1 rounded-full px-2 text-xs font-medium whitespace-nowrap forced-colors:border',
        style.chip,
        className,
      )}
    >
      <span aria-hidden className={clsx('size-1.5 shrink-0 rounded-full', style.dot)} />
      <span className="truncate">{name}</span>
      {auto ? <span className="sr-only">, added automatically</span> : null}
    </span>
  );
}

export interface LabelChipsProps {
  labels: readonly ConversationLabel[];
  /** Chips shown before the rest collapse into "+N". */
  max?: number;
  className?: string;
}

export function LabelChips({ labels, max = 3, className }: LabelChipsProps) {
  if (labels.length === 0) {
    return null;
  }
  const shown = labels.slice(0, max);
  const hidden = labels.slice(max);
  return (
    <span className={clsx('inline-flex max-w-full min-w-0 flex-wrap items-center gap-1', className)}>
      <span className="sr-only">Labels: </span>
      {shown.map((label) => (
        <LabelChip key={label.id} name={label.name} color={label.color} source={label.source} />
      ))}
      {hidden.length > 0 ? (
        <span
          title={hidden.map((label) => label.name).join(', ')}
          className="inline-flex h-5 items-center rounded-full bg-fill-muted px-1.5 text-xs font-medium text-fg-secondary"
        >
          +{hidden.length}
          <span className="sr-only"> more: {hidden.map((label) => label.name).join(', ')}</span>
        </span>
      ) : null}
    </span>
  );
}
