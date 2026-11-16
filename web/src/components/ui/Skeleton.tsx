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
  className?: string;
  style?: CSSProperties;
}

export function Skeleton({ shape = 'line', className, style }: SkeletonProps) {
  return <span aria-hidden className={clsx('block animate-pulse bg-fill-muted', shapes[shape], className)} style={style} />;
}
