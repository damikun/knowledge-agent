import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { describe, expect, it } from 'vitest';
import { BottomSheet, snapAfterDrag, type SheetSnap } from './BottomSheet';

const HEIGHTS: Record<SheetSnap, number> = { collapsed: 120, half: 400, full: 788 };

function Sheet({ initial = 'collapsed' }: { initial?: SheetSnap }) {
  const [snap, setSnap] = useState<SheetSnap>(initial);
  return (
    <div>
      <output aria-label="Snap">{snap}</output>
      <BottomSheet label="Settings" snap={snap} onSnapChange={setSnap} peek={120}>
        <button type="button">Inside</button>
      </BottomSheet>
    </div>
  );
}

describe('bottom sheet', () => {
  it('settles a slow drag at the nearest snap point and a fling at the next one in its direction', () => {
    expect(snapAfterDrag(HEIGHTS, 380, 0)).toBe('half');
    expect(snapAfterDrag(HEIGHTS, 700, 0.1)).toBe('full');
    expect(snapAfterDrag(HEIGHTS, 200, -0.1)).toBe('collapsed');
    expect(snapAfterDrag(HEIGHTS, 420, -0.8)).toBe('full');
    expect(snapAfterDrag(HEIGHTS, 380, 0.8)).toBe('collapsed');
    expect(snapAfterDrag(HEIGHTS, 788, -1.2)).toBe('full');
    expect(snapAfterDrag(HEIGHTS, 120, 1.2)).toBe('collapsed');
  });

  it('opens and closes with its handle, steps with the arrow keys, and goes full height with its button', async () => {
    const user = userEvent.setup();
    render(<Sheet />);
    const handle = screen.getByRole('button', { name: 'Settings' });
    const snap = screen.getByRole('status', { name: 'Snap' });
    expect(handle).toHaveAttribute('aria-expanded', 'false');
    expect(handle).toHaveAccessibleDescription('Drag the handle, or use the up and down arrow keys, to resize.');

    await user.click(handle);
    expect(snap).toHaveTextContent('half');
    expect(handle).toHaveAttribute('aria-expanded', 'true');
    await user.click(screen.getByRole('button', { name: 'Full height' }));
    expect(snap).toHaveTextContent('full');
    await user.click(screen.getByRole('button', { name: 'Half height' }));
    expect(snap).toHaveTextContent('half');

    handle.focus();
    await user.keyboard('{ArrowUp}');
    expect(snap).toHaveTextContent('full');
    await user.keyboard('{ArrowUp}');
    expect(snap).toHaveTextContent('full');
    await user.keyboard('{ArrowDown}{ArrowDown}');
    expect(snap).toHaveTextContent('collapsed');
    await user.keyboard('{Enter}');
    expect(snap).toHaveTextContent('half');
  });

  it('collapses on Esc from inside and keeps focus on its handle', async () => {
    const user = userEvent.setup();
    render(<Sheet initial="full" />);
    await user.click(screen.getByRole('button', { name: 'Inside' }));
    await user.keyboard('{Escape}');
    expect(screen.getByRole('status', { name: 'Snap' })).toHaveTextContent('collapsed');
    expect(screen.getByRole('button', { name: 'Settings' })).toHaveFocus();
  });
});
