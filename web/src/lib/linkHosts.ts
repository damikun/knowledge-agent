// Answers may only link to hosts the team chose. "example.com" also allows www.example.com, "*.example.com" its
// subdomains. Anything else is shown as text, so a link planted in a source needs a person to copy it.

export function isHostAllowed(host: string, allowed: readonly string[]): boolean {
  const name = host.toLowerCase().replace(/\.$/, '');
  return allowed.some((entry) => {
    if (entry.startsWith('*.')) {
      return name.endsWith(entry.slice(1));
    }
    return name === entry || name === `www.${entry}`;
  });
}

/** The URL when it is http(s) on an allowed host or `ownHost`, otherwise null. */
export function allowedLinkUrl(href: string | null | undefined, allowed: readonly string[], ownHost?: string): string | null {
  if (!href) {
    return null;
  }
  try {
    const url = new URL(href);
    if (url.protocol !== 'http:' && url.protocol !== 'https:') {
      return null;
    }
    const host = url.hostname.toLowerCase();
    return (ownHost && host === ownHost.toLowerCase()) || isHostAllowed(host, allowed) ? url.toString() : null;
  } catch {
    return null;
  }
}

/** The text a blocked link shows: its words, plus the address when they differ, so nothing is hidden. */
export function blockedLinkSuffix(href: string | null | undefined, text: string): string | null {
  if (!href) {
    return null;
  }
  const address = href.trim();
  return address && address !== text.trim() ? ` (${address})` : null;
}

interface TextNode {
  type: string;
  value?: string;
  children?: readonly TextNode[];
}

/** The plain text of a markdown (hast) node, such as a link's words. */
export function nodeText(node: TextNode | undefined): string {
  if (!node) {
    return '';
  }
  if (node.type === 'text' && typeof node.value === 'string') {
    return node.value;
  }
  return (node.children ?? []).map(nodeText).join('');
}
