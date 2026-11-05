import { FileText, Sparkles, Undo2 } from 'lucide-react';
import { useState } from 'react';
import { Link, useParams } from 'react-router';
import { Button } from '../../components/ui/Button';
import { errorMessage } from '../../lib/api';
import { useReturnState } from '../../lib/returnTo';
import type { Guid } from '../../lib/types';
import { isAdmin, useMe } from '../auth/api';
import { sourcesPageLink } from '../chat/CitationChip';
import { citedSources, isModelNotConfigured, useSuggestReply } from './suggestReplyApi';

export interface SuggestReplyProps {
  /** Defaults to the inbox route's conversation. */
  conversationId?: Guid;
  /** Lets a replacing draft be undone. */
  currentText: string;
  /** Never sends; only fills the reply box. */
  onDraft: (text: string) => void;
  disabled?: boolean;
}

/** Callers key this by conversation, since a draft belongs to one. */
export function SuggestReply({ conversationId, currentText, onDraft, disabled = false }: SuggestReplyProps) {
  const { conversationId: routeId } = useParams();
  const id = conversationId ?? routeId ?? null;
  const admin = isAdmin(useMe().data);
  const suggest = useSuggestReply();
  const returnState = useReturnState();
  const [replaced, setReplaced] = useState<string | null>(null);

  const draft = suggest.data;
  // Once the draft is sent or cleared, its sources no longer apply.
  const showSources = draft !== undefined && currentText.trim().length > 0;
  const sources = draft ? citedSources(draft.citations) : [];

  const run = () => {
    if (!id) {
      return;
    }
    const previous = currentText.trim() ? currentText : null;
    suggest.mutate(id, {
      onSuccess: (result) => {
        setReplaced(previous);
        onDraft(result.text);
      },
    });
  };

  return (
    <div className="flex flex-wrap items-center gap-x-3 gap-y-1 px-1">
      <Button type="button" variant="ghost" size="sm" icon={Sparkles} loading={suggest.isPending} disabled={disabled || !id} onClick={run}>
        {suggest.isPending ? 'Drafting a reply…' : 'Suggest reply'}
      </Button>
      <div role="status" className="min-w-0 flex-1 text-xs text-fg-muted">
        {suggest.isError ? (
          isModelNotConfigured(suggest.error) ? (
            <span>
              No chat model is connected, so no reply can be drafted.{' '}
              {admin ? (
                <Link to="/models" state={returnState} className="font-medium text-accent-text hover:underline">
                  Connect one in Models
                </Link>
              ) : (
                'Ask an admin to connect one.'
              )}
            </span>
          ) : (
            <span className="text-danger-text">{errorMessage(suggest.error)}</span>
          )
        ) : showSources ? (
          <span className="flex flex-wrap items-center gap-x-2 gap-y-1">
            <span>{sources.length > 0 ? 'Draft based on' : 'Draft without sources. Check it before you send.'}</span>
            {sources.map((source) => (
              <Link
                key={source.sourceId}
                to={sourcesPageLink(source.sourceId)}
                state={returnState}
                title={`${source.refs.map((ref) => `[${ref}]`).join(' ')} ${source.title}`}
                className="inline-flex max-w-56 items-center gap-1 rounded-md bg-surface-subtle px-1.5 py-0.5 text-fg-secondary hover:text-fg"
              >
                <FileText className="size-3 shrink-0" aria-hidden />
                <span className="truncate">{source.title}</span>
              </Link>
            ))}
          </span>
        ) : null}
      </div>
      {showSources && replaced !== null ? (
        <Button
          type="button"
          variant="ghost"
          size="sm"
          icon={Undo2}
          onClick={() => {
            onDraft(replaced);
            setReplaced(null);
          }}
        >
          Restore my text
        </Button>
      ) : null}
    </div>
  );
}
