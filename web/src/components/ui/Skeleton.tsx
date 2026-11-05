import { clsx } from 'clsx';
import type { CSSProperties } from 'react';

type SkeletonShape = 'line' | 'tile' | 'card';

const shapes: Record<SkeletonShape, string> = {
  line: 'rounded-full',
  tile: 'rounded-lg',
  card: 'rounded-2xl',
};

export interface SkeletonProps {
  shape?: SkeletonShape;
  /** The size, such as h-3.5 w-40; a width that varies per row goes in `style`. */
  className?: string;
  style?: CSSProperties;
}

/** Hidden from screen readers; pair it with one status message such as "Loading agents". */
export function Skeleton({ shape = 'line', className, style }: SkeletonProps) {
  return <span aria-hidden className={clsx('block animate-pulse bg-fill-muted', shapes[shape], className)} style={style} />;
}
