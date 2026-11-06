import axe from 'axe-core';

/** Color contrast is not checked because jsdom does not render; theme.test.ts checks the palette instead. */
export async function axeViolations(context: Element = document.body): Promise<string[]> {
  // jsdom has no canvas; axe only uses it for icon heuristics. Without this stub jsdom logs "not implemented" errors.
  HTMLCanvasElement.prototype.getContext = (() => null) as typeof HTMLCanvasElement.prototype.getContext;
  const results = await axe.run(context, {
    runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa', 'best-practice'] },
    resultTypes: ['violations'],
    // jsdom never loads a frame's page, and axe would wait for an answer from inside it.
    iframes: false,
    rules: {
      'color-contrast': { enabled: false },
      // Pages are rendered without the app shell in most tests, so landmarks are checked on the shell test only.
      region: { enabled: false },
    },
  });
  return results.violations.map(
    (violation) =>
      `${violation.id} (${violation.impact}): ${violation.help}: ${violation.nodes
        .slice(0, 3)
        .map((node) => node.target.join(' '))
        .join(' | ')}`,
  );
}
