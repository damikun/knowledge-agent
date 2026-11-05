import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { StatCard } from './StatCard';

describe('StatCard', () => {
  it('shows the number in tabular figures with its trend and context', () => {
    render(<StatCard label="Conversations" value="1,204" caption="Last 7 days" trend={{ direction: 'up', label: '+12% on last week', tone: 'success' }} />);

    expect(screen.getByText('Conversations')).toBeInTheDocument();
    expect(screen.getByText('1,204')).toHaveClass('text-kpi', 'tabular-nums');
    expect(screen.getByText('+12% on last week')).toHaveAttribute('data-tone', 'success');
    expect(screen.getByText('Last 7 days')).toBeInTheDocument();
  });

  it('leaves out the trend line when there is nothing to add', () => {
    const { container } = render(<StatCard label="Average rating" value="4.6" />);
    expect(container.querySelectorAll('p')).toHaveLength(2);
  });
});
