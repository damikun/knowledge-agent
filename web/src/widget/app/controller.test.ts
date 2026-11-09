import { describe, expect, it } from 'vitest';
import { statusLine } from './controller';

describe('statusLine', () => {
  it('keeps the last sentence of a note as plain text', () => {
    expect(statusLine("Thanks! I'll check the **returns policy** [S1].")).toBe("I'll check the returns policy.");
    expect(statusLine('Looking it up')).toBe('Looking it up');
  });

  it('has nothing to show for an empty note', () => {
    expect(statusLine('  \n ')).toBeNull();
  });

  it('shortens a long sentence', () => {
    const line = statusLine('a'.repeat(400));
    expect(line).toHaveLength(160);
    expect(line?.endsWith('…')).toBe(true);
  });
});
