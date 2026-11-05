import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { describe, expect, it, vi } from 'vitest';
import { axeViolations } from '../../test/a11y';
import { Select } from './Select';

type Kind = 'all' | 'url' | 'pdf' | 'text';

const OPTIONS = [
  { value: 'all', label: 'All types' },
  { value: 'url', label: 'Web page', hint: '12' },
  { value: 'pdf', label: 'PDF' },
  { value: 'text', label: 'Text' },
] as const;

function TypeFilter({ onChange }: { onChange?: (value: Kind) => void }) {
  const [kind, setKind] = useState<Kind>('all');
  return (
    <>
      <Select
        aria-label="Type"
        options={OPTIONS}
        value={kind}
        onChange={(next) => {
          setKind(next);
          onChange?.(next);
        }}
      />
      <button type="button">Next on the page</button>
    </>
  );
}

const trigger = () => screen.getByRole('combobox', { name: 'Type' });
const listbox = () => screen.getByRole('listbox', { name: 'Type' });

describe('Select', () => {
  it('is a select-only combobox that shows the chosen option', async () => {
    const user = userEvent.setup();
    render(<TypeFilter />);

    expect(trigger()).toHaveTextContent('All types');
    expect(trigger()).toHaveAttribute('aria-haspopup', 'listbox');
    expect(trigger()).toHaveAttribute('aria-expanded', 'false');
    expect(screen.queryByRole('listbox')).toBeNull();

    await user.click(trigger());
    expect(trigger()).toHaveAttribute('aria-expanded', 'true');
    expect(within(listbox()).getAllByRole('option').map((option) => option.textContent)).toEqual(['All types', 'Web page12', 'PDF', 'Text']);
    expect(within(listbox()).getByRole('option', { name: 'All types' })).toHaveAttribute('aria-selected', 'true');
    expect(await axeViolations()).toEqual([]);
  });

  it('chooses an option with the mouse and returns focus to the trigger', async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    render(<TypeFilter onChange={onChange} />);

    await user.click(trigger());
    await user.click(within(listbox()).getByRole('option', { name: 'PDF' }));
    expect(onChange).toHaveBeenCalledWith('pdf');
    expect(screen.queryByRole('listbox')).toBeNull();
    expect(trigger()).toHaveTextContent('PDF');
    await waitFor(() => expect(trigger()).toHaveFocus());
  });

  it('opens on the arrow keys at the chosen option, moves, and chooses with Enter', async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    render(<TypeFilter onChange={onChange} />);

    await user.tab();
    expect(trigger()).toHaveFocus();
    await user.keyboard('{ArrowDown}');
    await waitFor(() => expect(within(listbox()).getByRole('option', { name: 'All types' })).toHaveFocus());

    await user.keyboard('{ArrowDown}{ArrowDown}');
    expect(within(listbox()).getByRole('option', { name: 'PDF' })).toHaveFocus();
    await user.keyboard('{End}');
    expect(within(listbox()).getByRole('option', { name: 'Text' })).toHaveFocus();
    await user.keyboard('{Home}');
    expect(within(listbox()).getByRole('option', { name: 'All types' })).toHaveFocus();
    await user.keyboard('{ArrowUp}{Enter}');
    expect(onChange).toHaveBeenCalledWith('text');
    await waitFor(() => expect(trigger()).toHaveFocus());
    expect(trigger()).toHaveTextContent('Text');
  });

  it('closes on Esc without choosing', async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    render(<TypeFilter onChange={onChange} />);

    await user.click(trigger());
    await user.keyboard('{ArrowDown}{Escape}');
    expect(screen.queryByRole('listbox')).toBeNull();
    expect(onChange).not.toHaveBeenCalled();
    await waitFor(() => expect(trigger()).toHaveFocus());
  });

  it('jumps to a typed option while closed, like a native select', async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    render(<TypeFilter onChange={onChange} />);

    await user.tab();
    await user.keyboard('p');
    expect(onChange).toHaveBeenCalledWith('pdf');
    expect(screen.queryByRole('listbox')).toBeNull();
  });

  it('takes its name from a label and shows a placeholder for an unknown value', () => {
    render(
      <>
        <label htmlFor="mode">Mode</label>
        <Select id="mode" options={OPTIONS} value={'' as Kind} onChange={() => {}} placeholder="Pick a type" />
      </>,
    );
    expect(screen.getByRole('combobox', { name: 'Mode' })).toHaveTextContent('Pick a type');
  });
});
