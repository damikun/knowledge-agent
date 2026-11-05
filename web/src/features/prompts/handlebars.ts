// For syntax highlighting only; the API does the rendering.

export type TemplateTokenType = 'text' | 'variable' | 'block-open' | 'block-close' | 'else' | 'comment' | 'partial';

export interface TemplateToken {
  type: TemplateTokenType;
  value: string;
}

const TAG_PATTERN = /\{\{\{[\s\S]*?\}\}\}|\{\{!--[\s\S]*?--\}\}|\{\{[\s\S]*?\}\}/g;

function classify(tag: string): TemplateTokenType {
  const inner = tag.replace(/^\{\{\{?~?\s*/, '');
  if (inner.startsWith('!')) {
    return 'comment';
  }
  if (inner.startsWith('#') || inner.startsWith('^')) {
    return 'block-open';
  }
  if (inner.startsWith('/')) {
    return 'block-close';
  }
  if (inner.startsWith('>')) {
    return 'partial';
  }
  if (/^else\b/.test(inner)) {
    return 'else';
  }
  return 'variable';
}

export function tokenizeTemplate(template: string): TemplateToken[] {
  const tokens: TemplateToken[] = [];
  let last = 0;
  for (const match of template.matchAll(TAG_PATTERN)) {
    if (match.index > last) {
      tokens.push({ type: 'text', value: template.slice(last, match.index) });
    }
    tokens.push({ type: classify(match[0]), value: match[0] });
    last = match.index + match[0].length;
  }
  if (last < template.length) {
    tokens.push({ type: 'text', value: template.slice(last) });
  }
  return tokens;
}
