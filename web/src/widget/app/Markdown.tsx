import { memo, useMemo } from 'react';
import ReactMarkdown, { type Components, type Options } from 'react-markdown';
import remarkGfm from 'remark-gfm';
import { ScrollBox } from '../../components/ui/ScrollBox';
import { safeExternalUrl } from '../../lib/format';
import { allowedLinkUrl, blockedLinkSuffix, nodeText } from '../../lib/linkHosts';
import { markerNumber, remarkCitations } from './citations';
import { useWidget } from './context';
import type { CitationMode } from './types';

type RemarkPlugins = NonNullable<Options['remarkPlugins']>;

const NO_MARKERS: ReadonlyMap<string, number> = new Map();

function ownHost(): string | undefined {
  return typeof window === 'undefined' ? undefined : window.location.hostname;
}

export interface MarkdownProps {
  text: string;
  // Null leaves [S#] tags alone, for staff replies.
  citationMode: CitationMode | null;
  markers?: ReadonlyMap<string, number>;
  // Staff write their replies themselves, so their links need no allowlist.
  anyLink?: boolean;
}

// Raw HTML is never rendered: react-markdown shows it as text unless rehype-raw is added.
export const Markdown = memo(function Markdown({ text, citationMode, markers = NO_MARKERS, anyLink = false }: MarkdownProps) {
  const { t, config } = useWidget();
  const { linkHosts } = config;
  const plugins = useMemo<RemarkPlugins>(
    () => (citationMode ? [remarkGfm, [remarkCitations, { mode: citationMode, markers }]] : [remarkGfm]),
    [citationMode, markers],
  );
  const components = useMemo<Components>(
    () => ({
      a: ({ href, children, node, ...rest }) => {
        const number = markerNumber(href);
        if (number !== null) {
          return (
            <sup className="ka-marker">
              <span aria-hidden="true">{number}</span>
              <span className="sr-only">{t('source', { count: number })}</span>
            </sup>
          );
        }
        const url = anyLink ? safeExternalUrl(href) : allowedLinkUrl(href, linkHosts, ownHost());
        if (!url) {
          return (
            <span>
              {children}
              {blockedLinkSuffix(href, nodeText(node))}
            </span>
          );
        }
        return (
          <a {...rest} href={url} target="_blank" rel="noopener noreferrer">
            {children}
            <span className="sr-only"> ({t('newTab')})</span>
          </a>
        );
      },
      img: ({ alt }) => <span>{alt ? `[${alt}]` : null}</span>,
      table: ({ node: _node, ...props }) => (
        <ScrollBox className="ka-table">
          <table {...props} />
        </ScrollBox>
      ),
      pre: ({ node: _node, ...props }) => <ScrollBox as="pre" {...props} />,
    }),
    [t, linkHosts, anyLink],
  );
  return (
    <div className="ka-md">
      <ReactMarkdown remarkPlugins={plugins} components={components}>
        {text}
      </ReactMarkdown>
    </div>
  );
});
