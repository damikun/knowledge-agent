import { clsx } from 'clsx';
import { Fragment, memo, useMemo, type ReactNode } from 'react';
import ReactMarkdown, { type Components } from 'react-markdown';
import remarkGfm from 'remark-gfm';
import { ScrollBox } from '../../components/ui/ScrollBox';
import { safeExternalUrl } from '../../lib/format';
import { CitationGroup } from './CitationChip';
import { refsFromHref, remarkCitations } from './citations';

const remarkPlugins = [remarkGfm, remarkCitations];

function createComponents(renderCitation?: (ref: string) => ReactNode, renderLink?: RenderLink): Components {
  return {
    a: ({ href, children, node: _node, ...rest }) => {
      const refs = refsFromHref(href);
      if (refs.length > 0) {
        return renderCitation ? refs.map((ref) => <Fragment key={ref}>{renderCitation(ref)}</Fragment>) : <CitationGroup refs={refs} />;
      }
      const custom = href && renderLink ? renderLink(href, children) : null;
      if (custom) {
        return custom;
      }
      const url = safeExternalUrl(href);
      if (!url) {
        return <span>{children}</span>;
      }
      return (
        <a {...rest} href={url} target="_blank" rel="noopener noreferrer">
          {children}
        </a>
      );
    },
    table: ({ node: _node, ...props }) => (
      <ScrollBox className="relative overflow-x-auto">
        <table {...props} />
      </ScrollBox>
    ),
    pre: ({ node: _node, ...props }) => <ScrollBox as="pre" {...props} />,
    img: ({ node: _node, alt }) => <span className="text-fg-muted">[image{alt ? `: ${alt}` : ''}]</span>,
  };
}

const defaultComponents = createComponents();

/** Renders a link the default would not, such as a path in the portal; null keeps the default. */
export type RenderLink = (href: string, children: ReactNode) => ReactNode | null;

export interface MarkdownProps {
  text: string;
  variant?: 'default' | 'muted';
  /** Renders a [S#] tag instead of a numbered marker. Pass a memoized function, or the markdown re-renders every time. */
  renderCitation?: (ref: string) => ReactNode;
  /** Pass a memoized function, like renderCitation. */
  renderLink?: RenderLink;
}

export const Markdown = memo(function Markdown({ text, variant = 'default', renderCitation, renderLink }: MarkdownProps) {
  const components = useMemo(
    () => (renderCitation || renderLink ? createComponents(renderCitation, renderLink) : defaultComponents),
    [renderCitation, renderLink],
  );
  return (
    <div
      className={clsx(
        'prose prose-zinc prose-a:underline-offset-2',
        'prose-code:before:content-none prose-code:after:content-none prose-pre:rounded-md prose-pre:text-xs',
        'prose-headings:font-semibold prose-headings:tracking-tight',
        variant === 'muted'
          ? 'prose-sm max-w-none text-small leading-relaxed text-fg-secondary prose-p:my-1.5 prose-ul:my-1.5 prose-ol:my-1.5'
          : 'prose-sm max-w-[70ch] leading-[1.375rem] prose-p:my-2.5 prose-headings:mt-5 prose-headings:mb-2 prose-h1:text-base prose-h2:text-[0.9375rem] prose-h3:text-sm prose-ul:my-2.5 prose-ol:my-2.5 prose-li:my-1 prose-li:marker:text-fg-muted prose-pre:my-3 prose-pre:leading-relaxed prose-code:text-[0.8125rem] prose-blockquote:font-normal prose-blockquote:not-italic',
      )}
    >
      <ReactMarkdown remarkPlugins={remarkPlugins} components={components}>
        {text}
      </ReactMarkdown>
    </div>
  );
});
