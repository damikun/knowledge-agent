import { render } from '@testing-library/react';
import { UserRound } from 'lucide-react';
import { describe, expect, it } from 'vitest';
import { axeViolations } from '../../test/a11y';
import { Avatar, initials } from './Avatar';

describe('initials', () => {
  it('takes the first and the last word, and the local part of an e-mail address', () => {
    expect(initials('Ada Lovelace')).toBe('AL');
    expect(initials('Ada King Lovelace')).toBe('AL');
    expect(initials('  shop assistant ')).toBe('SA');
    expect(initials('sam.rivera@example.com')).toBe('S');
    expect(initials('Émile')).toBe('É');
    expect(initials('')).toBe('?');
  });
});

describe('Avatar', () => {
  it('shows initials, an icon or a picture, hidden from screen readers next to the name', async () => {
    const { container } = render(
      <ul>
        <li>
          <Avatar name="Ada Lovelace" size="sm" />
          Ada Lovelace
        </li>
        <li>
          <Avatar icon={UserRound} size="lg" />
          Anonymous visitor
        </li>
        <li>
          <Avatar name="Acme" src="/api/media/logo" shape="tile" />
          Acme
        </li>
      </ul>,
    );

    const [person, visitor, logo] = Array.from(container.querySelectorAll('li > span'));
    expect(person).toHaveAttribute('aria-hidden', 'true');
    expect(person).toHaveTextContent('AL');
    expect(person).toHaveClass('size-7', 'rounded-full');
    expect(visitor!.querySelector('svg')).not.toBeNull();
    expect(visitor).toHaveClass('size-10');
    expect(logo!.querySelector('img')).toHaveAttribute('src', '/api/media/logo');
    expect(logo).toHaveClass('rounded-lg');
    expect(await axeViolations()).toEqual([]);
  });
});
