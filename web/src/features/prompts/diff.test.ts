import { describe, expect, it } from 'vitest';
import { diffLines, diffStats, promptText } from './diff';

describe('diffLines', () => {
  it('marks kept, removed and added lines in order', () => {
    const lines = diffLines('a\nb\nc\nd', 'a\nc\nx\nd');
    expect(lines).toEqual([
      { type: 'same', text: 'a' },
      { type: 'removed', text: 'b' },
      { type: 'same', text: 'c' },
      { type: 'added', text: 'x' },
      { type: 'same', text: 'd' },
    ]);
    expect(diffStats(lines)).toEqual({ added: 1, removed: 1 });
  });

  it('handles empty texts and Windows line endings', () => {
    expect(diffLines('', '')).toEqual([]);
    expect(diffLines('', 'one')).toEqual([{ type: 'added', text: 'one' }]);
    expect(diffLines('one\r\ntwo', 'one\ntwo')).toEqual([
      { type: 'same', text: 'one' },
      { type: 'same', text: 'two' },
    ]);
  });

  it('shows a changed line as removed then added', () => {
    expect(diffLines('Hello {{name}}.', 'Hi {{name}}!')).toEqual([
      { type: 'removed', text: 'Hello {{name}}.' },
      { type: 'added', text: 'Hi {{name}}!' },
    ]);
  });
});

describe('promptText', () => {
  it('is the template of a text prompt and the messages under their roles for a chat prompt', () => {
    expect(promptText({ text: 'Hi', messages: [] })).toBe('Hi');
    expect(
      promptText({
        text: null,
        messages: [
          { role: 'system', content: 'Be brief.' },
          { role: 'user', content: '{{question}}' },
        ],
      }),
    ).toBe('[system]\nBe brief.\n\n[user]\n{{question}}');
  });
});
