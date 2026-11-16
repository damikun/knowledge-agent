import type { Placement } from '@floating-ui/react';

export function transformOriginOf(placement: Placement): string {
  const [side, alignment] = placement.split('-');
  return `${alignment === 'end' ? 'right' : 'left'} ${side === 'top' ? 'bottom' : 'top'}`;
}
