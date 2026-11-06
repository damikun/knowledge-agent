import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { describe, expect, it } from 'vitest';
import { axeViolations } from '../../test/a11y';
import { Button } from './Button';
import { ConfirmDialog, Modal } from './Modal';

function Rename({ dismissible = true }: { dismissible?: boolean }) {
  const [open, setOpen] = useState(false);
  return (
    <>
      <Button onClick={() => setOpen(true)}>Rename</Button>
      <Modal
        open={open}
        onClose={() => setOpen(false)}
        title="Rename the folder"
        description="Sources keep their place."
        dismissible={dismissible}
        footer={
          <>
            <Button onClick={() => setOpen(false)}>Cancel</Button>
            <Button variant="primary">Save</Button>
          </>
        }
      >
        <label>
          Name
          <input />
        </label>
      </Modal>
    </>
  );
}

describe('Modal', () => {
  it('is a named, described dialog that takes focus and keeps Tab inside', async () => {
    const user = userEvent.setup();
    render(<Rename />);
    await user.click(screen.getByRole('button', { name: 'Rename' }));

    const dialog = screen.getByRole('dialog', { name: 'Rename the folder' });
    expect(dialog).toHaveAttribute('aria-modal', 'true');
    expect(dialog).toHaveAccessibleDescription('Sources keep their place.');
    expect(screen.getByRole('button', { name: 'Close' })).toHaveFocus();

    await user.tab();
    expect(screen.getByRole('textbox', { name: 'Name' })).toHaveFocus();
    await user.tab();
    await user.tab();
    expect(screen.getByRole('button', { name: 'Save' })).toHaveFocus();
    await user.tab();
    expect(screen.getByRole('button', { name: 'Close' })).toHaveFocus();
    await user.tab({ shift: true });
    expect(screen.getByRole('button', { name: 'Save' })).toHaveFocus();
    expect(await axeViolations()).toEqual([]);
  });

  it('closes on Esc and gives focus back to the button that opened it', async () => {
    const user = userEvent.setup();
    render(<Rename />);
    const opener = screen.getByRole('button', { name: 'Rename' });
    await user.click(opener);

    await user.keyboard('{Escape}');
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(opener).toHaveFocus();
  });

  it('stays open on Esc while it may not be dismissed', async () => {
    const user = userEvent.setup();
    render(<Rename dismissible={false} />);
    await user.click(screen.getByRole('button', { name: 'Rename' }));

    await user.keyboard('{Escape}');
    expect(screen.getByRole('dialog', { name: 'Rename the folder' })).toBeInTheDocument();
  });

  it('starts a confirmation on Cancel, so Enter never deletes by accident', async () => {
    render(
      <ConfirmDialog
        open
        title="Delete the folder?"
        message="Its sources move to the top level."
        confirmLabel="Delete"
        onConfirm={() => {}}
        onCancel={() => {}}
      />,
    );

    expect(screen.getByRole('button', { name: 'Cancel' })).toHaveFocus();
  });
});
