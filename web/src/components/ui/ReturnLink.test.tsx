import { render, screen } from '@testing-library/react';
import type { ReactNode } from 'react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router';
import { afterEach, describe, expect, it } from 'vitest';
import { ReturnLink } from './ReturnLink';

function Address() {
  const location = useLocation();
  return <output data-testid="address">{`${location.pathname}${location.search}`}</output>;
}

function renderAt(entries: string[], ui: ReactNode) {
  return render(
    <MemoryRouter initialEntries={entries} initialIndex={entries.length - 1}>
      <Routes>
        <Route path="*" element={ui} />
      </Routes>
      <Address />
    </MemoryRouter>,
  );
}

afterEach(() => {
  window.history.replaceState(null, '');
});

describe('ReturnLink', () => {
  it('is a back arrow named for its origin, with the browser shortcuts as its tooltip', () => {
    renderAt(['/inbox'], <ReturnLink returnTo={{ label: 'Ada Lovelace', to: '/contacts/1' }} />);
    const link = screen.getByRole('link', { name: 'Back to Ada Lovelace' });
    expect(link).toHaveAttribute('href', '/contacts/1');
    expect(link).toHaveAttribute('title', 'Back to Ada Lovelace (Alt+← / ⌘[)');
  });

  it('shows the origin as text when given children', () => {
    renderAt(['/inbox'], <ReturnLink returnTo={{ label: 'Analytics', to: '/analytics?days=7' }}>Analytics</ReturnLink>);
    expect(screen.getByRole('link', { name: 'Analytics' })).toHaveAttribute('href', '/analytics?days=7');
  });

  it('opens the origin as a new entry when the history does not tell where it is', async () => {
    renderAt(['/inbox'], <ReturnLink returnTo={{ label: 'Analytics', to: '/analytics?days=7', idx: 0 }} />);
    await userEvent.click(screen.getByRole('link', { name: 'Back to Analytics' }));
    expect(screen.getByTestId('address')).toHaveTextContent('/analytics?days=7');
  });

  it('steps back through the history to the origin, so the history does not grow', async () => {
    // React Router keeps the entry's index in history.state.idx; here the third entry is open.
    window.history.replaceState({ idx: 2 }, '');
    // The link's address differs from the entry only so the test can tell a step back from a new entry.
    renderAt(['/contacts/1', '/inbox?c=1', '/inbox?c=2'], <ReturnLink returnTo={{ label: 'Ada Lovelace', to: '/contacts/1?new-entry', idx: 0 }} />);
    await userEvent.click(screen.getByRole('link', { name: 'Back to Ada Lovelace' }));
    expect(screen.getByTestId('address')).toHaveTextContent(/^\/contacts\/1$/);
  });
});
