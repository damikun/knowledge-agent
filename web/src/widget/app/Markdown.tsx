import { memo, useMemo } from 'react';
import ReactMarkdown, { type Components, type Options } from 'react-markdown';
import remarkGfm from 'remark-gfm';
import { ScrollBox } from '../../components/ui/ScrollBox';
import { markerNumber, remarkCitations } from './citations';
import { useWidget } from './context';
import type { CitationMode } from './types';

type RemarkPlugins = NonNullable<Options['remarkPlugins']>;

const NO_MARKERS: ReadonlyMap<string, number> = new Map();

function safeUrl(href: string | undefined): string | null {
  if (!href) {
    return null;
  }
  try {
    const url = new URL(href);
    return url.protocol === 'http:' || url.protocol === 'https:' ? url.toString() : null;
  } catch {
    return null;
  }
}

export interface MarkdownProps {
  text: string;
  // Null leaves [S#] tags alone, for staff replies.
  citationMode: CitationMode | null;
  markers?: ReadonlyMap<string, number>;
}

// Raw HTML is never rendered: react-markdown shows it as text unless rehype-raw is added.
export const Markdown = memo(function Markdown({ text, citationMode, markers = NO_MARKERS }: MarkdownProps) {
  const { t } = useWidget();
  const plugins = useMemo<RemarkPlugins>(
    () => (citationMode ? [remarkGfm, [remarkCitations, { mode: citationMode, markers }]] : [remarkGfm]),
    [citationMode, markers],
  );
  const components = useMemo<Components>(
    () => ({
      a: ({ href, children, node: _node, ...rest }) => {
        const number = markerNumber(href);
        if (number !== null) {
          return (
            <sup className="ka-marker">
              <span aria-hidden="true">{number}</span>
              <span className="sr-only">{t('source', { count: number })}</span>
            </sup>
          );
        }
        const url = safeUrl(href);
        if (!url) {
          return <span>{children}</span>;
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
    [t],
  );
  return (
    <div className="ka-md">
      <ReactMarkdown remarkPlugins={plugins} components={components}>
        {text}
      </ReactMarkdown>
    </div>
  );
});
