import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { CircleAlert, FolderInput, RefreshCw, Trash } from 'lucide-react';
import { useState } from 'react';
import { describe, expect, it, vi } from 'vitest';
import { axeViolations } from '../../test/a11y';
import { Menu } from './Menu';
import { ConfirmDialog } from './Modal';

function SourceRow({ onMove = () => {}, onReingest = () => {} }: { onMove?: () => void; onReingest?: () => void }) {
  const [confirming, setConfirming] = useState(false);
  return (
    <>
      <Menu
        label="Actions for “Handbook”"
        items={[
          { label: 'Move to folder', icon: FolderInput, onSelect: onMove },
          { label: 'Re-ingest', icon: RefreshCw, onSelect: onReingest },
          { label: 'Delete', icon: Trash, danger: true, onSelect: () => setConfirming(true) },
        ]}
      />
      <button type="button">Next on the page</button>
      <ConfirmDialog
        open={confirming}
        title="Delete source?"
        message="“Handbook” will be deleted."
        onConfirm={() => setConfirming(false)}
        onCancel={() => setConfirming(false)}
      />
    </>
  );
}

const trigger = () => screen.getByRole('button', { name: 'Actions for “Handbook”' });
const menu = () => screen.getByRole('menu', { name: 'Actions for “Handbook”' });
const item = (name: string) => within(menu()).getByRole('menuitem', { name });

describe('Menu', () => {
  it('is a menu button with a menu of items', async () => {
    const user = userEvent.setup();
    render(<SourceRow />);

    expect(trigger()).toHaveAttribute('aria-haspopup', 'menu');
    expect(trigger()).toHaveAttribute('aria-expanded', 'false');
    expect(screen.queryByRole('menu')).toBeNull();

    await user.click(trigger());
    expect(trigger()).toHaveAttribute('aria-expanded', 'true');
    expect(within(menu()).getAllByRole('menuitem').map((element) => element.textContent)).toEqual(['Move to folder', 'Re-ingest', 'Delete']);
    expect(await axeViolations()).toEqual([]);

    await user.click(trigger());
    expect(screen.queryByRole('menu')).toBeNull();
  });

  it('moves between the items with the arrow keys, Home and End, and closes on Esc', async () => {
    const user = userEvent.setup();
    render(<SourceRow />);

    trigger().focus();
    await user.keyboard('{Enter}');
    await waitFor(() => expect(item('Move to folder')).toHaveFocus());
    await user.keyboard('{ArrowDown}');
    expect(item('Re-ingest')).toHaveFocus();
    await user.keyboard('{End}');
    expect(item('Delete')).toHaveFocus();
    await user.keyboard('{ArrowDown}');
    expect(item('Move to folder')).toHaveFocus();
    await user.keyboard('{ArrowUp}');
    expect(item('Delete')).toHaveFocus();
    await user.keyboard('{Home}');
    expect(item('Move to folder')).toHaveFocus();

    await user.keyboard('{Escape}');
    expect(screen.queryByRole('menu')).toBeNull();
    expect(trigger()).toHaveFocus();
  });

  it('opens on the arrow keys at the first or the last item', async () => {
    const user = userEvent.setup();
    render(<SourceRow />);

    trigger().focus();
    await user.keyboard('{ArrowUp}');
    await waitFor(() => expect(item('Delete')).toHaveFocus());
    await user.keyboard('{Escape}');
    await user.keyboard('{ArrowDown}');
    await waitFor(() => expect(item('Move to folder')).toHaveFocus());
  });

  it('runs an item and hands focus back to the trigger', async () => {
    const user = userEvent.setup();
    const onReingest = vi.fn();
    render(<SourceRow onReingest={onReingest} />);

    trigger().focus();
    await user.keyboard('{Enter}');
    await waitFor(() => expect(item('Move to folder')).toHaveFocus());
    await user.keyboard('{ArrowDown}{Enter}');
    expect(onReingest).toHaveBeenCalledOnce();
    expect(screen.queryByRole('menu')).toBeNull();
    expect(trigger()).toHaveFocus();
  });

  it('leaves focus in a dialog that an item opens, which returns it to the trigger', async () => {
    const user = userEvent.setup();
    render(<SourceRow />);

    await user.click(trigger());
    await user.click(item('Delete'));
    const dialog = screen.getByRole('dialog', { name: 'Delete source?' });
    expect(screen.queryByRole('menu')).toBeNull();
    await waitFor(() => expect(within(dialog).getByRole('button', { name: 'Cancel' })).toHaveFocus());
    await new Promise(requestAnimationFrame);
    expect(within(dialog).getByRole('button', { name: 'Cancel' })).toHaveFocus();

    await user.click(within(dialog).getByRole('button', { name: 'Cancel' }));
    expect(trigger()).toHaveFocus();
  });

  it('closes on a click outside, and moves on with Tab', async () => {
    const user = userEvent.setup();
    render(<SourceRow />);

    await user.click(trigger());
    await user.click(document.body);
    expect(screen.queryByRole('menu')).toBeNull();

    trigger().focus();
    await user.keyboard('{Enter}');
    await waitFor(() => expect(item('Move to folder')).toHaveFocus());
    await user.tab();
    await waitFor(() => expect(screen.queryByRole('menu')).toBeNull());
    expect(trigger()).not.toHaveFocus();
  });

  it('opens from a trigger of its own and shows rich items', async () => {
    const user = userEvent.setup();
    const onSelect = vi.fn();
    render(
      <Menu
        label="Settings to fix"
        className="w-80"
        trigger={(props) => (
          <button {...props} type="button">
            <CircleAlert aria-hidden />2 to fix
          </button>
        )}
        items={[
          {
            label: (
              <>
                <strong>Name:</strong> Enter a name.
              </>
            ),
            onSelect,
          },
        ]}
      />,
    );

    await user.click(screen.getByRole('button', { name: '2 to fix' }));
    const list = screen.getByRole('menu', { name: 'Settings to fix' });
    await user.click(within(list).getByRole('menuitem', { name: 'Name: Enter a name.' }));
    expect(onSelect).toHaveBeenCalledOnce();
    expect(screen.getByRole('button', { name: '2 to fix' })).toHaveFocus();
  });
});
