import { describe, expect, it } from 'vitest';
import { citationHref, extractCitationRefs, refFromHref, refsFromHref, splitCitations } from './citations';

describe('splitCitations', () => {
  it('splits text around citation tags', () => {
    expect(splitCitations('Vacation is 25 days [S1].')).toEqual([
      { type: 'text', value: 'Vacation is 25 days ' },
      { type: 'citation', ref: 'S1' },
      { type: 'text', value: '.' },
    ]);
  });

  it('handles adjacent, grouped and multi-digit tags', () => {
    expect(splitCitations('[S1][S12] and [S3, S4; S5]')).toEqual([
      { type: 'citation', ref: 'S1' },
      { type: 'citation', ref: 'S12' },
      { type: 'text', value: ' and ' },
      { type: 'citation', ref: 'S3' },
      { type: 'citation', ref: 'S4' },
      { type: 'citation', ref: 'S5' },
    ]);
  });

  it('normalizes lowercase tags and inner whitespace', () => {
    expect(splitCitations('see [ s7 ]')).toEqual([
      { type: 'text', value: 'see ' },
      { type: 'citation', ref: 'S7' },
    ]);
  });

  it('leaves text without tags, other brackets and markdown links untouched', () => {
    expect(splitCitations('No citations here.')).toEqual([{ type: 'text', value: 'No citations here.' }]);
    expect(splitCitations('Array[0] and [Source 1] and [S]')).toEqual([{ type: 'text', value: 'Array[0] and [Source 1] and [S]' }]);
    expect(splitCitations('[S1](https://example.com) is a link')).toEqual([{ type: 'text', value: '[S1](https://example.com) is a link' }]);
  });

  it('returns an empty list for empty text', () => {
    expect(splitCitations('')).toEqual([]);
  });
});

describe('extractCitationRefs', () => {
  it('returns unique refs in order of first appearance', () => {
    expect(extractCitationRefs('A [S2]. B [S1][S2]. C [S3, S1].')).toEqual(['S2', 'S1', 'S3']);
  });
});

describe('citation hrefs', () => {
  it('round-trips refs through link hrefs and ignores regular links', () => {
    expect(refFromHref(citationHref('S9'))).toBe('S9');
    expect(refFromHref('https://example.com')).toBeNull();
    expect(refFromHref(undefined)).toBeNull();
  });
});

describe('refsFromHref', () => {
  it('reads the refs of a group of adjacent tags and of a single tag', () => {
    expect(refsFromHref(citationHref('S1,S3'))).toEqual(['S1', 'S3']);
    expect(refsFromHref(citationHref('S2'))).toEqual(['S2']);
    expect(refsFromHref('https://example.com')).toEqual([]);
  });
});
