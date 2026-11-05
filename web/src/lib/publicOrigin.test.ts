import { describe, expect, it } from 'vitest';
import { checkPublicAddress, PLACEHOLDER_ORIGIN, publicOrigin, usesPlaceholderOrigin } from './publicOrigin';

describe('checkPublicAddress', () => {
  it.each([
    ['https://support.acme.com', 'https://support.acme.com'],
    ['  https://Support.Acme.com/  ', 'https://support.acme.com'],
    ['https://acme.com/support/', 'https://acme.com/support'],
    ['http://localhost:8080', 'http://localhost:8080'],
    ['', null],
  ])('accepts %j as %j', (input, url) => {
    expect(checkPublicAddress(input)).toEqual({ url, error: null });
  });

  it.each(['support.acme.com', 'ftp://acme.com', '/support', 'https://acme.com/?team=1', 'https://acme.com/#top', 'https://admin:secret@acme.com'])(
    'rejects %j with a reason',
    (input) => {
      const result = checkPublicAddress(input);
      expect(result.url).toBeNull();
      expect(result.error).toBeTruthy();
    },
  );
});

describe('publicOrigin', () => {
  it('prefers the saved public address over the address of this page', () => {
    expect(publicOrigin('https://support.acme.com/')).toBe('https://support.acme.com');
    expect(usesPlaceholderOrigin('https://support.acme.com')).toBe(false);
  });

  it('shows the placeholder on localhost when nothing is saved or the saved value is unusable', () => {
    // The tests run on localhost.
    expect(publicOrigin(null)).toBe(PLACEHOLDER_ORIGIN);
    expect(publicOrigin('not an address')).toBe(PLACEHOLDER_ORIGIN);
    expect(usesPlaceholderOrigin()).toBe(true);
  });
});
