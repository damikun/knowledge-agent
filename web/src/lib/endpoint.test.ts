import { describe, expect, it } from 'vitest';
import { endpoint, pathOf, queryString } from './endpoint';

describe('endpoints', () => {
  it('fills path parameters, encoding them, and adds the query', () => {
    const message = endpoint('POST', '/api/conversations/{id}/messages/{messageId}');

    expect(pathOf(message, { id: 'a b', messageId: 7 })).toBe('/api/conversations/a%20b/messages/7');
    expect(pathOf(endpoint('GET', '/api/sources'), undefined, { folder: 'f1', page: 2 })).toBe('/api/sources?folder=f1&page=2');
  });

  it('leaves out empty query values and keeps commas readable', () => {
    expect(queryString({ ids: 'a,b', empty: '', none: null, missing: undefined, flag: false })).toBe('?ids=a,b&flag=false');
    expect(queryString({})).toBe('');
    expect(queryString(undefined)).toBe('');
  });

  it('refuses to build a path with a parameter left out', () => {
    const agent = endpoint('GET', '/api/agents/{id}');

    expect(() => pathOf(agent, {} as { id: string })).toThrow('Missing path parameter "id" for GET /api/agents/{id}.');
  });
});
