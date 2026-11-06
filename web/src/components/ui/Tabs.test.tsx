import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { describe, expect, it } from 'vitest';
import { axeViolations } from '../../test/a11y';
import { TabPanel, Tabs } from './Tabs';

const ITEMS = [
  { id: 'general', label: 'General' },
  { id: 'behavior', label: 'Behavior' },
  { id: 'advanced', label: 'Advanced' },
] as const;

type Section = (typeof ITEMS)[number]['id'];

function Sections() {
  const [tab, setTab] = useState<Section>('general');
  return (
    <>
      <Tabs idPrefix="agent" label="Agent sections" items={ITEMS} value={tab} onChange={setTab} />
      <TabPanel idPrefix="agent" id={tab}>
        <p>{tab} settings</p>
      </TabPanel>
    </>
  );
}

describe('Tabs', () => {
  it('is one tab stop; arrows, Home and End move and select', async () => {
    const user = userEvent.setup();
    render(<Sections />);

    await user.tab();
    expect(screen.getByRole('tab', { name: 'General' })).toHaveFocus();
    await user.keyboard('{ArrowRight}');
    expect(screen.getByRole('tab', { name: 'Behavior' })).toHaveFocus();
    expect(screen.getByRole('tab', { name: 'Behavior' })).toHaveAttribute('aria-selected', 'true');
    expect(screen.getByRole('tabpanel', { name: 'Behavior' })).toHaveTextContent('behavior settings');
    await user.keyboard('{End}');
    expect(screen.getByRole('tab', { name: 'Advanced' })).toHaveFocus();
    await user.keyboard('{ArrowRight}');
    expect(screen.getByRole('tab', { name: 'General' })).toHaveFocus();
    await user.keyboard('{ArrowLeft}{Home}');
    expect(screen.getByRole('tab', { name: 'General' })).toHaveFocus();

    await user.tab();
    expect(screen.getByRole('tabpanel', { name: 'General' })).toHaveFocus();
    expect(await axeViolations()).toEqual([]);
  });
});
