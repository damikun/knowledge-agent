import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { describe, expect, it, vi } from 'vitest';
import { axeViolations } from '../../test/a11y';
import { FilterChips } from './FilterChips';

type Status = 'all' | 'attention' | 'indexing';

const OPTIONS = [
  { value: 'all', label: 'All' },
  { value: 'attention', label: 'Needs attention' },
  { value: 'indexing', label: 'Indexing' },
] as const;

function StatusFilter({ onChange }: { onChange?: (value: Status) => void }) {
  const [status, setStatus] = useState<Status>('all');
  return (
    <FilterChips
      label="Status"
      options={OPTIONS}
      value={status}
      onChange={(next) => {
        setStatus(next);
        onChange?.(next);
      }}
    />
  );
}

describe('FilterChips', () => {
  it('is a named group of radio buttons with the current filter checked', async () => {
    render(<StatusFilter />);

    expect(screen.getByRole('radiogroup', { name: 'Status' })).toBeInTheDocument();
    expect(screen.getAllByRole('radio').map((radio) => radio.getAttribute('value'))).toEqual(['all', 'attention', 'indexing']);
    expect(screen.getByRole('radio', { name: 'All' })).toBeChecked();
    expect(await axeViolations()).toEqual([]);
  });

  it('changes the filter by click and by arrow keys', async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    render(<StatusFilter onChange={onChange} />);

    await user.click(screen.getByText('Needs attention'));
    expect(screen.getByRole('radio', { name: 'Needs attention' })).toBeChecked();

    await user.keyboard('{ArrowRight}');
    expect(screen.getByRole('radio', { name: 'Indexing' })).toBeChecked();
    expect(onChange.mock.calls).toEqual([['attention'], ['indexing']]);
  });

  it('shows counts after the labels and reads them with the name', () => {
    render(
      <FilterChips
        label="Status"
        options={[
          { value: 'all', label: 'All', count: 1240 },
          { value: 'attention', label: 'Needs attention', count: 3, dot: 'danger' },
        ]}
        value="all"
        onChange={() => {}}
      />,
    );
    expect(screen.getByRole('radio', { name: /^All, ?1,240$/ })).toBeChecked();
    expect(screen.getByRole('radio', { name: /^Needs attention, ?3$/ })).not.toBeChecked();
  });
});
