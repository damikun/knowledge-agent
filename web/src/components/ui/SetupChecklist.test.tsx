import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { axeViolations } from '../../test/a11y';
import { renderWithProviders } from '../../test/utils';
import { SetupChecklist, type SetupStep } from './SetupChecklist';

const createAgent = vi.fn();

const steps: SetupStep[] = [
  { id: 'model', title: 'Connect a model', description: 'Add an API key for a model provider.', state: 'done', action: { label: 'Add a key', to: '/models' } },
  { id: 'knowledge', title: 'Add knowledge', description: 'A website, files or text.', state: 'current', action: { label: 'Add a source', to: '/sources' } },
  { id: 'agent', title: 'Create an agent', state: 'todo', action: { label: 'Create an agent', onClick: createAgent } },
];

describe('SetupChecklist', () => {
  it('shows the progress and each step with its state and the way to do it', async () => {
    const user = userEvent.setup();
    renderWithProviders(<SetupChecklist title="Set up your agent" steps={steps} />);

    const checklist = screen.getByRole('region', { name: 'Set up your agent' });
    expect(checklist).toHaveTextContent('1 of 3 done');
    expect(within(checklist).getAllByRole('listitem')).toHaveLength(2);
    await user.click(within(checklist).getByRole('button', { name: '1 finished step' }));
    const [knowledge, agent, model] = within(checklist).getAllByRole('listitem');
    expect(model).toHaveTextContent('Connect a model, done');
    expect(knowledge).toHaveAttribute('aria-current', 'step');
    expect(knowledge).toHaveTextContent('Add knowledge, current step');
    expect(agent).toHaveTextContent('Create an agent, to do');
    expect(await axeViolations()).toEqual([]);

    expect(within(model!).queryByRole('link')).toBeNull();
    expect(within(knowledge!).getByRole('link', { name: 'Add a source' })).toHaveAttribute('href', '/sources');
    expect(within(knowledge!).getByRole('link', { name: 'Add a source' })).toHaveClass('bg-accent');
    expect(within(agent!).getByRole('button', { name: 'Create an agent' })).toHaveAttribute('data-variant', 'ghost');
    await user.click(within(agent!).getByRole('button', { name: 'Create an agent' }));
    expect(createAgent).toHaveBeenCalledOnce();
  });
});
