import { act, fireEvent, render, screen, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { axeViolations } from '../../test/a11y';
import { dismissToast, toast, Toaster } from './Toast';

const shown: number[] = [];

function show(...args: Parameters<typeof toast>) {
  let id = 0;
  act(() => {
    id = toast(...args);
  });
  shown.push(id);
  return id;
}

function notifications() {
  return within(screen.getByRole('region', { name: 'Notifications' })).queryAllByRole('listitem');
}

beforeEach(() => {
  vi.useFakeTimers();
});

afterEach(() => {
  act(() => shown.splice(0).forEach(dismissToast));
  vi.useRealTimers();
});

describe('Toaster', () => {
  it('announces a toast in a live region that is always on the page', async () => {
    render(<Toaster />);
    expect(screen.getByRole('list')).toHaveAttribute('aria-live', 'polite');
    expect(notifications()).toHaveLength(0);

    show({ title: 'Widget saved', description: 'Sites that embed it update within a minute.', tone: 'success' });

    const [item] = notifications();
    expect(item).toHaveTextContent('Widget saved');
    expect(item).toHaveTextContent('Sites that embed it update within a minute.');
    expect(item).toHaveAttribute('data-tone', 'success');
    vi.useRealTimers();
    expect(await axeViolations()).toEqual([]);
  });

  it('closes after five seconds, but not while the pointer is on it', () => {
    render(<Toaster />);
    show('Saved');

    act(() => vi.advanceTimersByTime(3000));
    fireEvent.pointerEnter(screen.getByRole('list'));
    act(() => vi.advanceTimersByTime(10_000));
    expect(notifications()).toHaveLength(1);

    fireEvent.pointerLeave(screen.getByRole('list'));
    act(() => vi.advanceTimersByTime(1900));
    expect(notifications()).toHaveLength(1);
    act(() => vi.advanceTimersByTime(200));
    expect(notifications()).toHaveLength(0);
  });

  it('runs its action or closes on request', () => {
    const undo = vi.fn();
    render(<Toaster />);
    show({ title: 'Source deleted', action: { label: 'Undo', onClick: undo } });
    show('Folder created');

    fireEvent.click(screen.getByRole('button', { name: 'Undo' }));
    expect(undo).toHaveBeenCalledOnce();
    expect(notifications().map((item) => item.textContent)).toEqual(['Folder created']);

    fireEvent.click(screen.getByRole('button', { name: 'Dismiss' }));
    expect(notifications()).toHaveLength(0);
  });

  it('shows the three newest at most', () => {
    render(<Toaster />);
    ['One', 'Two', 'Three', 'Four'].forEach((title) => show(title));
    expect(notifications().map((item) => item.textContent)).toEqual(['Two', 'Three', 'Four']);
  });
});
