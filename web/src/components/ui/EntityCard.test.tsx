import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { BookOpen, Wrench } from 'lucide-react';
import { Route } from 'react-router';
import { describe, expect, it, vi } from 'vitest';
import { axeViolations } from '../../test/a11y';
import { renderWithProviders } from '../../test/utils';
import { Badge } from './Badge';
import { Button } from './Button';
import { EntityCard } from './EntityCard';

describe('EntityCard', () => {
  it('opens its page from the title, which names the card, while its actions stay separate', async () => {
    const user = userEvent.setup();
    const onDelete = vi.fn();
    renderWithProviders(
      <ul>
        <li>
          <EntityCard
            title="Shop assistant"
            description="Answers shoppers on the demo store."
            to="/agents/shop"
            status={<Badge tone="success">Live</Badge>}
            actions={<Button variant="ghost" size="sm" onClick={onDelete}>Delete</Button>}
            meta={[
              { icon: BookOpen, label: '14 sources' },
              { icon: Wrench, label: '7 tools' },
            ]}
          />
        </li>
      </ul>,
      { route: '/agents', path: '/agents', extraRoutes: <Route path="/agents/shop" element={<p>Shop assistant page</p>} /> },
    );

    const card = screen.getByRole('article', { name: 'Shop assistant' });
    expect(within(card).getByRole('heading', { level: 3, name: 'Shop assistant' })).toBeInTheDocument();
    expect(within(card).getByText('SA')).toHaveAttribute('aria-hidden', 'true');
    expect(within(card).getByText('Answers shoppers on the demo store.')).toBeInTheDocument();
    expect(within(card).getAllByRole('listitem').map((item) => item.textContent)).toEqual(['14 sources', '7 tools']);
    expect(await axeViolations()).toEqual([]);

    await user.click(within(card).getByRole('button', { name: 'Delete' }));
    expect(onDelete).toHaveBeenCalledOnce();
    expect(screen.queryByText('Shop assistant page')).toBeNull();

    await user.click(screen.getByRole('link', { name: 'Shop assistant' }));
    expect(screen.getByText('Shop assistant page')).toBeInTheDocument();
  });

  it('opens through a handler, or not at all, and shows a given visual', async () => {
    const user = userEvent.setup();
    const onOpen = vi.fn();
    renderWithProviders(
      <>
        <EntityCard title="Demo shop" onOpen={onOpen} titleAs="h2" visual={<span data-testid="launcher" />} />
        <EntityCard title="Slack" />
      </>,
    );

    await user.click(screen.getByRole('button', { name: 'Demo shop' }));
    expect(onOpen).toHaveBeenCalledOnce();
    expect(screen.getByTestId('launcher')).toBeInTheDocument();
    expect(within(screen.getByRole('article', { name: 'Slack' })).getByRole('heading', { level: 3 })).toHaveTextContent('Slack');
    expect(screen.queryByRole('link')).toBeNull();
    expect(screen.getAllByRole('button')).toHaveLength(1);
  });

  it('passes its id and ARIA attributes through', () => {
    renderWithProviders(
      <>
        <EntityCard title="Email" id="integration-email" aria-describedby="email-status" />
        <EntityCard title="Slack" aria-label="Slack integration" aria-busy />
        <p id="email-status">Connected</p>
      </>,
    );

    const email = screen.getByRole('article', { name: 'Email' });
    expect(email).toHaveAttribute('id', 'integration-email');
    expect(email).toHaveAccessibleDescription('Connected');
    expect(screen.getByRole('article', { name: 'Slack integration' })).toHaveAttribute('aria-busy', 'true');
  });
});
