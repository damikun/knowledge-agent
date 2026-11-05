import { clsx } from 'clsx';
import type { ComponentProps } from 'react';

/* Name each table with aria-label or a caption. */

export interface TableProps extends ComponentProps<'table'> {
  /** Give it a height to keep the header row in view. */
  containerClassName?: string;
}

// Separate borders, because collapsed ones scroll away from a sticky header row.
export function Table({ containerClassName, className, ...props }: TableProps) {
  return (
    <div className={clsx('relative w-full overflow-x-auto', containerClassName)}>
      <table className={clsx('w-full border-separate border-spacing-0 text-small', className)} {...props} />
    </div>
  );
}

export function TableHead({ className, children, ...props }: ComponentProps<'thead'>) {
  return (
    <thead className={className} {...props}>
      <tr>{children}</tr>
    </thead>
  );
}

export interface TableHeaderProps extends ComponentProps<'th'> {
  numeric?: boolean;
}

export function TableHeader({ numeric = false, className, ...props }: TableHeaderProps) {
  return (
    <th
      scope="col"
      className={clsx(
        'sticky top-0 z-10 h-9 border-b border-border bg-surface px-3 text-xs font-medium whitespace-nowrap text-fg-muted first:pl-6 last:pr-6',
        numeric ? 'text-right' : 'text-left',
        className,
      )}
      {...props}
    />
  );
}

export interface TableRowProps extends ComponentProps<'tr'> {
  selected?: boolean;
}

export function TableRow({ selected = false, className, ...props }: TableRowProps) {
  return (
    <tr
      data-selected={selected || undefined}
      className={clsx('group/row h-10', selected ? 'bg-fill-active' : 'hover:bg-fill-hover', className)}
      {...props}
    />
  );
}

export interface TableCellProps extends ComponentProps<'td'> {
  numeric?: boolean;
  mono?: boolean;
}

function BodyCell({ as: Cell, className, ...props }: ComponentProps<'td'> & { as: 'td' | 'th' }) {
  return (
    <Cell className={clsx('border-b border-border-subtle px-3 py-2 align-middle group-last/row:border-b-0 first:pl-6 last:pr-6', className)} {...props} />
  );
}

export function TableCell({ numeric = false, mono = false, className, ...props }: TableCellProps) {
  return <BodyCell as="td" className={clsx('text-fg-secondary', numeric && 'text-right tabular-nums', mono && 'font-mono text-xs', className)} {...props} />;
}

/** Screen readers announce this cell with each cell of the row. */
export function TableRowHeader({ className, ...props }: ComponentProps<'th'>) {
  return <BodyCell as="th" scope="row" className={clsx('text-left font-medium text-fg', className)} {...props} />;
}
