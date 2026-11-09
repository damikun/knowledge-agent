import { describe, expect, it } from 'vitest';
import { allowedLinkUrl, blockedLinkSuffix, isHostAllowed } from './linkHosts';

describe('link hosts', () => {
  it('allows listed hosts, their www. form and wildcard subdomains', () => {
    expect(isHostAllowed('example.com', ['example.com'])).toBe(true);
    expect(isHostAllowed('www.example.com', ['example.com'])).toBe(true);
    expect(isHostAllowed('shop.example.com', ['example.com'])).toBe(false);
    expect(isHostAllowed('shop.example.com', ['*.example.com'])).toBe(true);
    expect(isHostAllowed('example.com.evil.test', ['example.com', '*.example.com'])).toBe(false);
    expect(isHostAllowed('evilexample.com', ['*.example.com'])).toBe(false);
  });

  it('returns the URL only for http(s) links to allowed hosts or the own host', () => {
    expect(allowedLinkUrl('https://example.com/a', ['example.com'])).toBe('https://example.com/a');
    expect(allowedLinkUrl('https://evil.test/f?d=1', ['example.com'])).toBeNull();
    expect(allowedLinkUrl('https://portal.test/inbox', [], 'portal.test')).toBe('https://portal.test/inbox');
    expect(allowedLinkUrl('javascript:alert(1)', ['example.com'])).toBeNull();
    expect(allowedLinkUrl(undefined, ['example.com'])).toBeNull();
  });

  it('shows the address of a blocked link when its words differ', () => {
    expect(blockedLinkSuffix('https://evil.test/f', 'Download form')).toBe(' (https://evil.test/f)');
    expect(blockedLinkSuffix('https://evil.test/f', 'https://evil.test/f')).toBeNull();
  });
});
