import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { describe, expect, it } from 'vitest';
import { axeViolations } from '../../test/a11y';
import { CollapsibleSection } from './CollapsibleSection';

describe('CollapsibleSection', () => {
  it('keeps its settings folded away until opened', async () => {
    const user = userEvent.setup();
    render(
      <form aria-label="Model">
        <CollapsibleSection title="Advanced" description="Temperature and token limits">
          <label htmlFor="temperature">Temperature</label>
          <input id="temperature" className="field" />
        </CollapsibleSection>
      </form>,
    );

    expect(screen.getByLabelText('Temperature')).not.toBeVisible();
    expect(await axeViolations()).toEqual([]);

    await user.click(screen.getByText('Advanced'));
    expect(screen.getByLabelText('Temperature')).toBeVisible();
  });

  it('can start open', () => {
    render(
      <CollapsibleSection title="Advanced" defaultOpen>
        <p>Reasoning effort</p>
      </CollapsibleSection>,
    );
    expect(screen.getByText('Reasoning effort')).toBeVisible();
  });

  it('opens from outside when controlled, and reports when people close it', async () => {
    const user = userEvent.setup();
    const changes: boolean[] = [];
    function KeyField() {
      const [open, setOpen] = useState(false);
      return (
        <>
          <button type="button" onClick={() => setOpen(true)}>
            Show the key
          </button>
          <CollapsibleSection
            title="Advanced"
            open={open}
            onOpenChange={(next) => {
              changes.push(next);
              setOpen(next);
            }}
          >
            <p>website-helper</p>
          </CollapsibleSection>
        </>
      );
    }
    render(<KeyField />);

    expect(screen.getByText('website-helper')).not.toBeVisible();
    await user.click(screen.getByRole('button', { name: 'Show the key' }));
    expect(screen.getByText('website-helper')).toBeVisible();

    await user.click(screen.getByText('Advanced'));
    await waitFor(() => expect(screen.getByText('website-helper')).not.toBeVisible());
    await waitFor(() => expect(changes.at(-1)).toBe(false));
  });
});
