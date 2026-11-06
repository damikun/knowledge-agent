import { createElement, type HTMLAttributes } from 'react';
import { useScrollFocus } from '../../lib/hooks/useScrollFocus';

export interface ScrollBoxProps extends HTMLAttributes<HTMLElement> {
  as?: 'div' | 'pre' | 'ol';
}

/** A box with its own scrolling, such as a code block. It joins the tab order only while its content overflows. */
export function ScrollBox({ as = 'div', ...props }: ScrollBoxProps) {
  const scroll = useScrollFocus<HTMLElement>();
  return createElement(as, { ...props, ...scroll });
}
