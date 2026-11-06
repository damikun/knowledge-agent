import { describe, expect, it } from 'vitest';
import { readFrameLanguage } from './host';

describe('frame language', () => {
  it('takes the language the loader passes from the host page', () => {
    expect(readFrameLanguage({ search: '?lang=de-AT' })).toBe('de-AT');
    expect(readFrameLanguage({ search: '?lang=sk' })).toBe('sk');
  });

  it('falls back to English, the language of the default texts, when none or nonsense is passed', () => {
    expect(readFrameLanguage({ search: '' })).toBe('en');
    expect(readFrameLanguage({ search: '?lang=%3Cscript%3E' })).toBe('en');
  });
});
