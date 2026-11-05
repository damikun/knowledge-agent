import { LoaderCircle } from 'lucide-react';
import { useId, useMemo, type ReactNode } from 'react';
import { numberCitations, stripPartialTag, type NumberedSource } from './citations';
import { useWidget } from './context';
import type { StreamingAnswer } from './controller';
import { Markdown } from './Markdown';
import type { Citation } from './types';

function initialOf(name: string): string {
  return [...name.trim()][0]?.toUpperCase() ?? '?';
}

export interface AvatarProps {
  name: string;
  imageUrl?: string | null;
  size?: 'sm' | 'md';
  // The header may have a color of its own; there the initial takes the header's text color.
  inHeader?: boolean;
}

export function Avatar({ name, imageUrl, size = 'sm', inHeader = false }: AvatarProps) {
  const dimension = size === 'md' ? 'size-9 text-sm' : 'size-7 text-xs';
  if (imageUrl) {
    return <img src={imageUrl} alt="" className={`${dimension} shrink-0 rounded-full bg-raised object-cover ring-1 ring-line`} />;
  }
  const colors = inHeader ? 'bg-header-ink/12 text-header-ink' : 'bg-linear-to-br from-user to-user-end text-user-ink shadow-sm';
  return (
    <span aria-hidden="true" className={`${dimension} ${colors} grid shrink-0 place-items-center rounded-full font-semibold`}>
      {initialOf(name)}
    </span>
  );
}

function BotAvatar() {
  const { config } = useWidget();
  return <Avatar name={config.title} imageUrl={config.avatarUrl} />;
}

export function VisitorBubble({ text, sending = false }: { text: string; sending?: boolean }) {
  const { t, mark } = useWidget();
  return (
    <div className="flex justify-end">
      <p
        {...mark('messages.visitor')}
        className={`max-w-[82%] rounded-[20px] rounded-br-md bg-linear-to-br from-user to-user-end px-3.5 py-2 text-[14.5px] leading-snug break-words whitespace-pre-wrap text-user-ink shadow-[0_1px_2px_rgb(0_0_0/0.1)] ${sending ? 'opacity-80' : ''}`}
      >
        <span className="sr-only">{t('you')}: </span>
        {text}
      </p>
    </div>
  );
}

export interface AssistantRowProps {
  children: ReactNode;
  footer?: ReactNode;
  label?: string;
  editable?: boolean;
}

export function AssistantRow({ children, footer, label, editable = false }: AssistantRowProps) {
  const { config, mark } = useWidget();
  return (
    <div className="flex items-start gap-2">
      <BotAvatar />
      <div className="min-w-0 max-w-[85%] flex-1">
        <div
          {...(editable ? mark('messages.assistant') : {})}
          className="w-fit max-w-full rounded-[20px] rounded-tl-md bg-bot px-3.5 py-2 text-[14.5px] leading-snug text-bot-ink"
        >
          <span className="sr-only">{label ?? config.title}: </span>
          {children}
        </div>
        {footer}
      </div>
    </div>
  );
}

export function StaffRow({ name, text }: { name: string | null; text: string }) {
  const { t } = useWidget();
  const shown = name ?? t('team');
  return (
    <div className="flex items-start gap-2">
      <span aria-hidden="true" className="grid size-7 shrink-0 place-items-center rounded-full bg-ink text-xs font-semibold text-panel">
        {initialOf(shown)}
      </span>
      <div className="min-w-0 max-w-[85%] flex-1">
        <p className="mb-1 pl-1 text-xs font-medium text-muted">{shown}</p>
        <div className="w-fit max-w-full rounded-[20px] rounded-tl-md bg-bot px-3.5 py-2 text-[14.5px] leading-snug text-bot-ink">
          <span className="sr-only">{shown}: </span>
          <Markdown text={text} citationMode={null} />
        </div>
      </div>
    </div>
  );
}

export function SystemNote({ text }: { text: string }) {
  return <p className="px-6 text-center text-xs leading-relaxed text-muted">{text}</p>;
}

export function ThinkingDots() {
  const { t } = useWidget();
  return (
    <span className="flex h-5 items-center gap-1" role="img" aria-label={t('thinking')}>
      {[0, 150, 300].map((delay) => (
        <span key={delay} className="size-1.5 rounded-full bg-muted motion-safe:animate-pulse" style={{ animationDelay: `${delay}ms` }} />
      ))}
    </span>
  );
}

function SourceList({ sources, linked }: { sources: NumberedSource[]; linked: boolean }) {
  const { t, mark } = useWidget();
  const headingId = useId();
  return (
    <div {...mark('behavior.citations')} className="mt-2.5 border-t border-line pt-2">
      <p id={headingId} className="text-[11px] font-semibold tracking-wide text-muted uppercase">
        {t('sources')}
      </p>
      <ol aria-labelledby={headingId} className="mt-1.5 space-y-1 text-[12.5px] leading-snug">
        {sources.map((source) => (
          <li key={source.number} className="flex items-baseline gap-2">
            <span aria-hidden="true" className="grid size-4 shrink-0 place-items-center rounded-full bg-accent-soft text-[10px] font-semibold text-accent-text tabular-nums">
              {source.number}
            </span>
            {linked && source.url ? (
              <a href={source.url} target="_blank" rel="noopener noreferrer" className="break-words text-accent-text underline decoration-1 underline-offset-2">
                {source.title}
                <span className="sr-only"> ({t('newTab')})</span>
              </a>
            ) : (
              <span className="break-words">{source.title}</span>
            )}
          </li>
        ))}
      </ol>
    </div>
  );
}

const NO_CITATIONS: Citation[] = [];

export function AnswerContent({ text, citations = NO_CITATIONS, streaming = false }: { text: string; citations?: Citation[]; streaming?: boolean }) {
  const { config } = useWidget();
  const mode = config.citationMode;
  const shown = streaming ? stripPartialTag(text) : text;
  const numbers = useMemo(() => (mode === 'hidden' ? null : numberCitations(shown, citations)), [mode, shown, citations]);
  return (
    <>
      <Markdown text={shown} citationMode={mode} {...(numbers ? { markers: numbers.markers } : {})} />
      {numbers && numbers.sources.length > 0 ? <SourceList sources={numbers.sources} linked={mode === 'links'} /> : null}
    </>
  );
}

export function StreamingAnswerView({ answer }: { answer: StreamingAnswer }) {
  const { t } = useWidget();
  const hasText = answer.text.trim() !== '';
  return (
    <AssistantRow editable>
      {hasText ? <AnswerContent text={answer.text} citations={answer.citations} streaming={!answer.done} /> : null}
      {answer.activity === 'searching' ? (
        <p className={`flex items-center gap-1.5 text-xs text-muted ${hasText ? 'mt-2' : ''}`}>
          <LoaderCircle className="size-3.5 motion-safe:animate-spin" aria-hidden="true" />
          {t('searching')}
        </p>
      ) : null}
      {answer.activity === 'thinking' && !hasText ? <ThinkingDots /> : null}
      {answer.error ? <p className={`text-sm text-danger ${hasText ? 'mt-2' : ''}`}>{answer.error}</p> : null}
    </AssistantRow>
  );
}
