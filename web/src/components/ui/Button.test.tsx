import { render, screen } from '@testing-library/react';
import { Trash2 } from 'lucide-react';
import { describe, expect, it } from 'vitest';
import { axeViolations } from '../../test/a11y';
import { Button } from './Button';

describe('Button', () => {
  it('is a quiet delete trigger in the danger-ghost variant, and turns gray when disabled', async () => {
    render(
      <>
        <Button variant="danger-ghost" icon={Trash2}>
          Delete agent
        </Button>
        <Button variant="danger-ghost" size="icon-sm" icon={Trash2} aria-label="Delete Shop help" disabled />
      </>,
    );

    const trigger = screen.getByRole('button', { name: 'Delete agent' });
    expect(trigger).toHaveClass('text-danger-text', 'hover:bg-danger-subtle');
    expect(trigger).not.toHaveClass('bg-danger');
    const disabled = screen.getByRole('button', { name: 'Delete Shop help' });
    expect(disabled).toBeDisabled();
    expect(disabled).toHaveClass('text-fg-disabled');
    expect(disabled).not.toHaveClass('text-danger-text');
    expect(await axeViolations()).toEqual([]);
  });
});
