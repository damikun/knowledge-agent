import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { describe, expect, it, vi } from 'vitest';
import { axeViolations } from '../../test/a11y';
import { renderWithProviders } from '../../test/utils';
import { Button } from './Button';
import { SideSheet } from './SideSheet';

function Page({ modal, onOutside }: { modal?: boolean; onOutside?: () => void }) {
  const [open, setOpen] = useState(false);
  return (
    <main>
      <Button onClick={() => setOpen(true)}>Open details</Button>
      <Button onClick={onOutside}>Elsewhere on the page</Button>
      <SideSheet
        open={open}
        onClose={() => setOpen(false)}
        modal={modal}
        title="Employee handbook"
        description="A PDF in the Legal folder."
        footer={
          <>
            <Button onClick={() => setOpen(false)}>Cancel</Button>
            <Button variant="primary">Save</Button>
          </>
        }
      >
        <label htmlFor="sheet-title">Title</label>
        <input id="sheet-title" className="field" defaultValue="Employee handbook" />
      </SideSheet>
    </main>
  );
}

describe('SideSheet', () => {
  it('opens as a modal dialog that keeps focus inside and gives it back when closed', async () => {
    const user = userEvent.setup();
    renderWithProviders(<Page />);

    const opener = screen.getByRole('button', { name: 'Open details' });
    await user.click(opener);
    const sheet = screen.getByRole('dialog', { name: 'Employee handbook' });
    expect(sheet).toHaveAttribute('aria-modal', 'true');
    expect(sheet).toHaveAccessibleDescription('A PDF in the Legal folder.');
    expect(sheet).toContainElement(document.activeElement as HTMLElement);
    expect(await axeViolations()).toEqual([]);

    // Close, Title, Cancel, Save, and around again.
    await user.tab();
    await user.tab();
    await user.tab();
    await user.tab();
    expect(screen.getByRole('button', { name: 'Close' })).toHaveFocus();

    await user.keyboard('{Escape}');
    expect(screen.queryByRole('dialog')).toBeNull();
    expect(opener).toHaveFocus();
  });

  it('leaves the page usable beside a non-modal sheet', async () => {
    const user = userEvent.setup();
    const onOutside = vi.fn();
    renderWithProviders(<Page modal={false} onOutside={onOutside} />);

    await user.click(screen.getByRole('button', { name: 'Open details' }));
    const sheet = screen.getByRole('dialog', { name: 'Employee handbook' });
    expect(sheet).not.toHaveAttribute('aria-modal');

    await user.click(screen.getByRole('button', { name: 'Elsewhere on the page' }));
    expect(onOutside).toHaveBeenCalledOnce();
    expect(sheet).toBeInTheDocument();

    await user.click(screen.getByRole('textbox', { name: 'Title' }));
    await user.keyboard('{Escape}');
    expect(screen.queryByRole('dialog')).toBeNull();
  });
});
