export function isHostAllowed(host: string, allowed: readonly string[]): boolean {
  const name = host.toLowerCase().replace(/\.$/, '');
  return allowed.some((entry) => {
    if (entry.startsWith('*.')) {
      return name.endsWith(entry.slice(1));
    }
    return name === entry || name === `www.${entry}`;
  });
}

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

export function nodeText(node: TextNode | undefined): string {
  if (!node) {
    return '';
  }
  if (node.type === 'text' && typeof node.value === 'string') {
    return node.value;
  }
  return (node.children ?? []).map(nodeText).join('');
}
