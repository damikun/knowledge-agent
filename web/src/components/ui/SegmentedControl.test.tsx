import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Monitor, Smartphone } from 'lucide-react';
import { useState } from 'react';
import { describe, expect, it, vi } from 'vitest';
import { axeViolations } from '../../test/a11y';
import { SegmentedControl } from './SegmentedControl';

type Size = 'desktop' | 'mobile' | 'tablet';

const OPTIONS = [
  { value: 'desktop', label: 'Desktop', icon: Monitor },
  { value: 'tablet', label: 'Tablet' },
  { value: 'mobile', label: 'Mobile', icon: Smartphone },
] as const;

function Preview({ onChange }: { onChange?: (value: Size) => void }) {
  const [size, setSize] = useState<Size>('desktop');
  return (
    <SegmentedControl
      label="Preview size"
      options={OPTIONS}
      value={size}
      onChange={(next) => {
        setSize(next);
        onChange?.(next);
      }}
    />
  );
}

describe('SegmentedControl', () => {
  it('is a group of radio buttons with the current mode checked', async () => {
    render(<Preview />);

    expect(screen.getByRole('radiogroup', { name: 'Preview size' })).toBeInTheDocument();
    expect(screen.getByRole('radio', { name: 'Desktop' })).toBeChecked();
    expect(screen.getByRole('radio', { name: 'Mobile' })).not.toBeChecked();
    expect(await axeViolations()).toEqual([]);
  });

  it('changes the mode by click and by arrow keys', async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    render(<Preview onChange={onChange} />);

    await user.click(screen.getByText('Mobile'));
    expect(screen.getByRole('radio', { name: 'Mobile' })).toBeChecked();

    await user.keyboard('{ArrowLeft}');
    expect(screen.getByRole('radio', { name: 'Tablet' })).toBeChecked();
    expect(onChange.mock.calls).toEqual([['mobile'], ['tablet']]);
  });
});
