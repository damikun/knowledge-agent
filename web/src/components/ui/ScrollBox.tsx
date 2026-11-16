import { createElement, type HTMLAttributes } from 'react';
import { useScrollFocus } from '../../lib/hooks/useScrollFocus';

export interface ScrollBoxProps extends HTMLAttributes<HTMLElement> {
  as?: 'div' | 'pre' | 'ol';
}

export function ScrollBox({ as = 'div', ...props }: ScrollBoxProps) {
  const scroll = useScrollFocus<HTMLElement>();
  return createElement(as, { ...props, ...scroll });
}
