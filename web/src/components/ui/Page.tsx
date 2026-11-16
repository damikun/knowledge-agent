import { clsx } from 'clsx';
import type { ReactNode } from 'react';

export { PageHeader } from './feedback';

type PageWidth = 'full' | 'narrow';

const widths: Record<PageWidth, string> = {
  full: '',
  narrow: 'max-w-3xl',
};

export interface PageProps {
  variant?: 'content' | 'workspace';
  width?: PageWidth;
  className?: string;
  children: ReactNode;
}

export function Page({ variant = 'content', width = 'full', className, children }: PageProps) {
  if (variant === 'workspace') {
    return <div className={clsx('flex h-full min-h-0 flex-col', className)}>{children}</div>;
  }
  return (
    <div className="h-full overflow-y-auto">
      <div className={clsx('w-full space-y-8 px-4 py-6 lg:px-6 [&>:first-child]:mb-5', widths[width], className)}>{children}</div>
    </div>
  );
}


export interface CardGridProps {
  label?: string;
  className?: string;
  children: ReactNode;
}

export function CardGrid({ label, className, children }: CardGridProps) {
  const Grid = label ? 'ul' : 'div';
  return (
    <Grid
      aria-label={label}
      aria-hidden={label ? undefined : true}
      className={clsx('grid grid-cols-[repeat(auto-fill,minmax(min(100%,20rem),1fr))] gap-4', className)}
    >
      {children}
    </Grid>
  );
}
