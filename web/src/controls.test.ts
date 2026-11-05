/// <reference types="node" />
import { readdirSync, readFileSync } from 'node:fs';
import { join, relative, resolve } from 'node:path';
import { describe, expect, it } from 'vitest';

// A native <select> draws its own arrow, cuts long labels off and shows a focus ring after a click.
// Use Select or FilterSelect.

const SRC = resolve(process.cwd(), 'src');

// The shared controls are where the native elements may live, the widget has its own look, and tests may render anything.
const SKIPPED = [/^components\/ui\//, /^widget\//, /^test\//, /\.test\.tsx$/];

const NATIVE_SELECT = /<select(?![\w-])/g;

function sourceFiles(directory: string): string[] {
  return readdirSync(directory, { withFileTypes: true }).flatMap((entry) => {
    const path = join(directory, entry.name);
    return entry.isDirectory() ? sourceFiles(path) : entry.name.endsWith('.tsx') ? [relative(SRC, path).split('\\').join('/')] : [];
  });
}

function findings(): string[] {
  return sourceFiles(SRC)
    .filter((file) => !SKIPPED.some((pattern) => pattern.test(file)))
    .flatMap((file) =>
      readFileSync(join(SRC, file), 'utf8')
        .split('\n')
        .flatMap((line, index) => (line.match(NATIVE_SELECT) ? [`${file}:${index + 1}`] : [])),
    );
}

describe('native selects', () => {
  it('finds a select element and leaves Select and similar names alone', () => {
    expect('<select id="a"> <select\n'.match(NATIVE_SELECT)).toHaveLength(2);
    expect('<Select value={v} /> <FilterSelect /> <selection> <select-menu>'.match(NATIVE_SELECT)).toBeNull();
  });

  it('appear nowhere in the portal', () => {
    expect(findings(), 'Use Select or FilterSelect from components/ui instead of a native <select>.').toEqual([]);
  });
});
