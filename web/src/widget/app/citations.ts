import type { Link, Nodes, Parent, PhrasingContent, Root, Text } from 'mdast';
import type { Citation, CitationMode } from './types';

// Answers cite search hits as [S12]; grouped forms such as [S1, S2] or [S1; S2] occur too.
const CITATION_PATTERN = /\[\s*([Ss]\d+(?:\s*[,;]\s*[Ss]\d+)*)\s*\]/g;
const SEPARATOR = /\s*[,;]\s*/;
// While an answer streams, a tag can be cut off at the end, such as "[S" or "[S1, S".
const PARTIAL_TAG = /\[\s*(?:[Ss]\d*(?:\s*[,;]\s*(?:[Ss]\d*)?)*)?$/;
const MARKER_PREFIX = '#ka-source-';

export interface NumberedSource {
  number: number;
  title: string;
  url: string | null;
}

export interface CitationNumbers {
  // Ref (S3) to the number shown in the text; refs of the same source share a number.
  markers: ReadonlyMap<string, number>;
  sources: NumberedSource[];
}

export function stripPartialTag(text: string): string {
  return text.replace(PARTIAL_TAG, '');
}

function refsIn(value: string): string[] {
  return value.split(SEPARATOR).map((ref) => ref.trim().toUpperCase()).filter(Boolean);
}

// Unique refs in order of first appearance; a tag followed by "(" is a markdown link, not a citation.
function citedRefs(text: string): string[] {
  const refs = new Set<string>();
  for (const match of text.matchAll(CITATION_PATTERN)) {
    if (text.charAt(match.index + match[0].length) !== '(') {
      refsIn(match[1] ?? '').forEach((ref) => refs.add(ref));
    }
  }
  return [...refs];
}

export function numberCitations(text: string, citations: readonly Citation[]): CitationNumbers {
  const byRef = new Map(citations.map((citation) => [citation.ref.toUpperCase(), citation] as const));
  const bySource = new Map<string, NumberedSource>();
  const markers = new Map<string, number>();
  for (const ref of citedRefs(text)) {
    const citation = byRef.get(ref);
    if (!citation) {
      continue;
    }
    const key = `${citation.url ?? ''}\n${citation.title}`;
    let source = bySource.get(key);
    if (!source) {
      source = { number: bySource.size + 1, title: citation.title, url: citation.url };
      bySource.set(key, source);
    }
    markers.set(ref, source.number);
  }
  return { markers, sources: [...bySource.values()] };
}

export function markerNumber(href: string | null | undefined): number | null {
  if (!href?.startsWith(MARKER_PREFIX)) {
    return null;
  }
  const number = Number.parseInt(href.slice(MARKER_PREFIX.length), 10);
  return Number.isFinite(number) ? number : null;
}

function markerLink(number: number): Link {
  return { type: 'link', url: `${MARKER_PREFIX}${number}`, title: null, children: [{ type: 'text', value: String(number) }] };
}

function isParent(node: Nodes): node is Nodes & Parent {
  return 'children' in node && Array.isArray((node as Parent).children);
}

function replaceTags(value: string, mode: CitationMode, markers: ReadonlyMap<string, number>): PhrasingContent[] | null {
  const parts: PhrasingContent[] = [];
  let last = 0;
  let found = false;
  for (const match of value.matchAll(CITATION_PATTERN)) {
    if (value.charAt(match.index + match[0].length) === '(') {
      continue;
    }
    found = true;
    let before = value.slice(last, match.index);
    const numbers = mode === 'hidden' ? [] : [...new Set(refsIn(match[1] ?? '').map((ref) => markers.get(ref)).filter((n) => n !== undefined))];
    if (numbers.length === 0) {
      // A removed tag takes the space before it along: "thirty days [S1]." reads "thirty days.".
      before = before.replace(/[ \t]+$/, '');
    }
    if (before) {
      parts.push({ type: 'text', value: before } satisfies Text);
    }
    numbers.forEach((number) => parts.push(markerLink(number)));
    last = match.index + match[0].length;
  }
  if (!found) {
    return null;
  }
  if (last < value.length) {
    parts.push({ type: 'text', value: value.slice(last) } satisfies Text);
  }
  return parts;
}

function transform(node: Parent, mode: CitationMode, markers: ReadonlyMap<string, number>): void {
  const children = node.children as Nodes[];
  for (let i = 0; i < children.length; i++) {
    const child = children[i]!;
    if (child.type === 'text') {
      const replacement = replaceTags(child.value, mode, markers);
      if (replacement) {
        children.splice(i, 1, ...replacement);
        i += replacement.length - 1;
      }
    } else if (child.type !== 'link' && child.type !== 'linkReference' && isParent(child)) {
      // Code has no text children, so tags in code stay literal.
      transform(child, mode, markers);
    }
  }
}

export interface RemarkCitationOptions {
  mode: CitationMode;
  markers: ReadonlyMap<string, number>;
}

// Hidden mode drops [S#] tags; the other modes turn them into numbered marker links, and drop tags with no source.
export function remarkCitations(options: RemarkCitationOptions) {
  return (tree: Root) => {
    transform(tree, options.mode, options.markers);
  };
}

// For screen reader announcements: the answer without markdown syntax or citation tags.
const SPACED_TAG = new RegExp(`[ \\t]*${CITATION_PATTERN.source}`, 'g');

export function plainText(markdown: string): string {
  return markdown
    .replace(SPACED_TAG, '')
    .replace(/```[\s\S]*?```/g, ' ')
    .replace(/!\[([^\]]*)\]\([^)]*\)/g, '$1')
    .replace(/\[([^\]]+)\]\([^)]*\)/g, '$1')
    .replace(/[*_`~#>|]/g, '')
    .replace(/\s+/g, ' ')
    .trim();
}
