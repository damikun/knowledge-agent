import type { Link, Nodes, Parent, PhrasingContent, Root, Text } from 'mdast';

export type CitationSegment = { type: 'text'; value: string } | { type: 'citation'; ref: string };

// The agent cites search hits as [S12]; grouped forms such as [S1, S2] or [S1; S2] are accepted too.
const CITATION_PATTERN = /\[\s*([Ss]\d+(?:\s*[,;]\s*[Ss]\d+)*)\s*\]/g;
const SEPARATOR = /\s*[,;]\s*/;
const CITATION_HREF_PREFIX = '#cite-';

function normalizeRef(ref: string): string {
  return ref.trim().toUpperCase();
}

export function splitCitations(text: string): CitationSegment[] {
  const segments: CitationSegment[] = [];
  let last = 0;
  for (const match of text.matchAll(CITATION_PATTERN)) {
    const index = match.index;
    // A following "(" means a markdown link such as [S1](https://...), not a citation.
    if (text.charAt(index + match[0].length) === '(') {
      continue;
    }
    if (index > last) {
      segments.push({ type: 'text', value: text.slice(last, index) });
    }
    for (const ref of (match[1] ?? '').split(SEPARATOR)) {
      if (ref) {
        segments.push({ type: 'citation', ref: normalizeRef(ref) });
      }
    }
    last = index + match[0].length;
  }
  if (last < text.length) {
    segments.push({ type: 'text', value: text.slice(last) });
  }
  return segments;
}

export function extractCitationRefs(text: string): string[] {
  const refs = new Set<string>();
  for (const segment of splitCitations(text)) {
    if (segment.type === 'citation') {
      refs.add(segment.ref);
    }
  }
  return [...refs];
}

export function citationHref(ref: string): string {
  return `${CITATION_HREF_PREFIX}${ref}`;
}

export function refFromHref(href: string | null | undefined): string | null {
  return href && href.startsWith(CITATION_HREF_PREFIX) ? href.slice(CITATION_HREF_PREFIX.length) : null;
}

/** The refs of a citation link; tags next to each other, such as [S1][S3], share one link. */
export function refsFromHref(href: string | null | undefined): string[] {
  return (refFromHref(href) ?? '').split(',').filter(Boolean);
}

function citationLink(refs: string[]): Link {
  return { type: 'link', url: citationHref(refs.join(',')), title: null, children: [{ type: 'text', value: refs.join(', ') }] };
}

/** Tags with nothing but spaces between them become one group, so the answer shows one cluster of markers. */
function groupSegments(segments: CitationSegment[]): Array<{ type: 'text'; value: string } | { type: 'citations'; refs: string[] }> {
  const groups: Array<{ type: 'text'; value: string } | { type: 'citations'; refs: string[] }> = [];
  for (let i = 0; i < segments.length; i++) {
    const segment = segments[i]!;
    const last = groups[groups.length - 1];
    if (segment.type === 'citation') {
      if (last?.type === 'citations') {
        last.refs.push(segment.ref);
      } else {
        groups.push({ type: 'citations', refs: [segment.ref] });
      }
      continue;
    }
    const next = segments[i + 1];
    if (last?.type === 'citations' && next?.type === 'citation' && segment.value.trim() === '') {
      continue;
    }
    groups.push({ type: 'text', value: segment.value });
  }
  return groups;
}

function isParent(node: Nodes): node is Nodes & Parent {
  return 'children' in node && Array.isArray((node as Parent).children);
}

function transform(node: Parent): void {
  const children = node.children as Nodes[];
  for (let i = 0; i < children.length; i++) {
    const child = children[i]!;
    if (child.type === 'text') {
      const segments = splitCitations((child as Text).value);
      if (!segments.some((s) => s.type === 'citation')) {
        continue;
      }
      const replacement: PhrasingContent[] = groupSegments(segments).map((s) =>
        s.type === 'text' ? ({ type: 'text', value: s.value } satisfies Text) : citationLink(s.refs),
      );
      children.splice(i, 1, ...replacement);
      i += replacement.length - 1;
    } else if (child.type !== 'link' && child.type !== 'linkReference' && isParent(child)) {
      // Code (inline and fenced) has no text children, so tags in code stay literal.
      transform(child);
    }
  }
}

/** remark plugin that turns [S#] tags into citation links, which Markdown renders as numbered markers. */
export function remarkCitations() {
  return (tree: Root) => {
    transform(tree);
  };
}
