import { clsx } from 'clsx';
import { useMemo } from 'react';
import { tokenizeTemplate, type TemplateTokenType } from './handlebars';
import { ScrollBox } from '../../components/ui/ScrollBox';

const TOKEN_CLASSES: Record<TemplateTokenType, string> = {
  text: '',
  variable: 'rounded-sm bg-accent-subtle text-accent-text',
  'block-open': 'rounded-sm bg-fill-muted font-semibold text-fg',
  'block-close': 'rounded-sm bg-fill-muted font-semibold text-fg',
  else: 'rounded-sm bg-fill-muted font-semibold text-fg',
  partial: 'rounded-sm bg-fill-muted text-fg',
  comment: 'text-fg-muted italic',
};

export function TemplateView({ template, label, className }: { template: string; label?: string; className?: string }) {
  const tokens = useMemo(() => tokenizeTemplate(template), [template]);
  return (
    <ScrollBox
      as="pre"
      aria-label={label}
      className={clsx(
        'overflow-auto rounded-lg border border-border-subtle bg-surface-subtle px-3.5 py-3 font-mono text-small leading-5 break-words whitespace-pre-wrap text-fg-secondary',
        className,
      )}
    >
      {tokens.map((token, index) =>
        token.type === 'text' ? (
          <span key={index}>{token.value}</span>
        ) : (
          <span key={index} className={TOKEN_CLASSES[token.type]} data-token={token.type}>
            {token.value}
          </span>
        ),
      )}
    </ScrollBox>
  );
}
