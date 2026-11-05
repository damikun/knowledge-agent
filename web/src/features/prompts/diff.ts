// Line diff via longest common subsequence.

type DiffLineType = 'same' | 'added' | 'removed';

export interface DiffLine {
  type: DiffLineType;
  text: string;
}

// Above this many line pairs the table gets too large; everything then shows as removed and added.
const MAX_CELLS = 4_000_000;

function splitLines(text: string): string[] {
  return text === '' ? [] : text.replace(/\r\n?/g, '\n').split('\n');
}

export function diffLines(before: string, after: string): DiffLine[] {
  const a = splitLines(before);
  const b = splitLines(after);

  // Skip the common head and tail so the table covers only the changed middle.
  let start = 0;
  while (start < a.length && start < b.length && a[start] === b[start]) {
    start++;
  }
  let endA = a.length;
  let endB = b.length;
  while (endA > start && endB > start && a[endA - 1] === b[endB - 1]) {
    endA--;
    endB--;
  }

  const head: DiffLine[] = a.slice(0, start).map((text) => ({ type: 'same', text }));
  const tail: DiffLine[] = a.slice(endA).map((text) => ({ type: 'same', text }));
  const midA = a.slice(start, endA);
  const midB = b.slice(start, endB);

  if (midA.length * midB.length > MAX_CELLS) {
    return [...head, ...midA.map((text) => ({ type: 'removed' as const, text })), ...midB.map((text) => ({ type: 'added' as const, text })), ...tail];
  }

  // lengths[i][j]: LCS length of midA[i..] and midB[j..].
  const width = midB.length + 1;
  const lengths = new Uint32Array((midA.length + 1) * width);
  for (let i = midA.length - 1; i >= 0; i--) {
    for (let j = midB.length - 1; j >= 0; j--) {
      lengths[i * width + j] = midA[i] === midB[j] ? lengths[(i + 1) * width + j + 1]! + 1 : Math.max(lengths[(i + 1) * width + j]!, lengths[i * width + j + 1]!);
    }
  }

  const middle: DiffLine[] = [];
  let i = 0;
  let j = 0;
  while (i < midA.length && j < midB.length) {
    if (midA[i] === midB[j]) {
      middle.push({ type: 'same', text: midA[i]! });
      i++;
      j++;
    } else if (lengths[(i + 1) * width + j]! >= lengths[i * width + j + 1]!) {
      middle.push({ type: 'removed', text: midA[i]! });
      i++;
    } else {
      middle.push({ type: 'added', text: midB[j]! });
      j++;
    }
  }
  for (; i < midA.length; i++) {
    middle.push({ type: 'removed', text: midA[i]! });
  }
  for (; j < midB.length; j++) {
    middle.push({ type: 'added', text: midB[j]! });
  }

  return [...head, ...middle, ...tail];
}

export function diffStats(lines: readonly DiffLine[]): { added: number; removed: number } {
  return {
    added: lines.filter((line) => line.type === 'added').length,
    removed: lines.filter((line) => line.type === 'removed').length,
  };
}

/** The template, or each chat message under its role. */
export function promptText(prompt: { text: string | null; messages: ReadonlyArray<{ role: string; content: string }> }): string {
  if (prompt.text !== null && prompt.messages.length === 0) {
    return prompt.text;
  }
  return prompt.messages.map((message) => `[${message.role}]\n${message.content}`).join('\n\n');
}
