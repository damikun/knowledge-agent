import type { Placement } from '@floating-ui/react';

/** A floating panel grows out of its anchor's side, so it scales from the corner nearest the anchor. */
export function transformOriginOf(placement: Placement): string {
  const [side, alignment] = placement.split('-');
  return `${alignment === 'end' ? 'right' : 'left'} ${side === 'top' ? 'bottom' : 'top'}`;
}
