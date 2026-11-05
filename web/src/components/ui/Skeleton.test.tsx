import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { axeViolations } from '../../test/a11y';
import { Skeleton } from './Skeleton';

describe('Skeleton', () => {
  it('stands in for content without reaching screen readers, which hear the status instead', async () => {
    const { container } = render(
      <div>
        <p role="status" className="sr-only">
          Loading agents
        </p>
        <Skeleton shape="card" className="h-40" />
        <Skeleton className="h-3.5" style={{ width: '60%' }} />
      </div>,
    );

    const [card, line] = Array.from(container.querySelectorAll('[aria-hidden="true"]'));
    expect(card).toHaveClass('animate-pulse', 'rounded-2xl', 'h-40');
    expect(line).toHaveClass('rounded-full', 'h-3.5');
    expect(line).toHaveStyle({ width: '60%' });
    expect(screen.getByRole('status')).toHaveTextContent('Loading agents');
    expect(await axeViolations()).toEqual([]);
  });
});
