import { describe, expect, it } from 'vitest';
import { isSafeReturnPath, readReturnTo, returnTitle } from './returnTo';

describe('returnTo', () => {
  it('accepts only relative paths inside the portal', () => {
    expect(isSafeReturnPath('/inbox?view=all&c=1')).toBe(true);
    expect(isSafeReturnPath('/contacts/0199c1e2')).toBe(true);
    expect(isSafeReturnPath('/analytics?days=7#top')).toBe(true);

    for (const unsafe of ['', 'inbox', 'https://evil.example/', '//evil.example/x', '/\\evil.example', '/x\\y', 'javascript:alert(1)', '/\t/evil.example', '/a\nb', 42, null, undefined]) {
      expect(isSafeReturnPath(unsafe), String(unsafe)).toBe(false);
    }
    expect(isSafeReturnPath(`/${'a'.repeat(2048)}`)).toBe(false);
  });

  it('reads a valid return from router state and drops anything else', () => {
    expect(readReturnTo({ returnTo: { label: ' Ada Lovelace ', to: '/contacts/1', idx: 3 } })).toEqual({ label: 'Ada Lovelace', to: '/contacts/1', idx: 3 });
    expect(readReturnTo({ returnTo: { label: 'Inbox', to: '/inbox', idx: -1 } })).toEqual({ label: 'Inbox', to: '/inbox' });
    expect(readReturnTo({ returnTo: { label: 'Inbox', to: '/inbox', idx: 1.5 } })).toEqual({ label: 'Inbox', to: '/inbox' });
    expect(readReturnTo({ returnTo: { label: 'x'.repeat(200), to: '/' } })?.label).toHaveLength(120);

    expect(readReturnTo(null)).toBeNull();
    expect(readReturnTo('returnTo')).toBeNull();
    expect(readReturnTo({ notice: 'Saved' })).toBeNull();
    expect(readReturnTo({ returnTo: null })).toBeNull();
    expect(readReturnTo({ returnTo: { label: '', to: '/inbox' } })).toBeNull();
    expect(readReturnTo({ returnTo: { label: 'Evil', to: 'https://evil.example' } })).toBeNull();
    expect(readReturnTo({ returnTo: { label: 'Evil', to: '//evil.example' } })).toBeNull();
  });

  it('names the browser shortcuts in the tooltip', () => {
    expect(returnTitle('Analytics')).toBe('Back to Analytics (Alt+← / ⌘[)');
  });
});
