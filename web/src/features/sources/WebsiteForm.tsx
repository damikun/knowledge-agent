import { useMutation, useQueryClient } from '@tanstack/react-query';
import { Earth, FileText, FolderTree, Plus, X } from 'lucide-react';
import { useId, useState, type FormEvent } from 'react';
import { fieldAria } from '../../components/ui/aria';
import { Button } from '../../components/ui/Button';
import { ChoiceCard, ChoiceCardGroup } from '../../components/ui/ChoiceCard';
import { CollapsibleSection } from '../../components/ui/CollapsibleSection';
import { Alert, ErrorMessage } from '../../components/ui/feedback';
import { Field, inputProps } from '../../components/ui/Field';
import { ApiError } from '../../lib/api';
import type { Guid } from '../../lib/types';
import { sourceKeys, sourcesApi } from './api';
import type { AddUrlSourceRequest, CrawlScope, SourceDto } from './types';

export const DEFAULT_MAX_PAGES = 100;

const SCOPES: ReadonlyArray<{ value: CrawlScope; title: string; description: string; icon: typeof FileText }> = [
  { value: 'page', title: 'This page only', description: 'Index just this address.', icon: FileText },
  {
    value: 'section',
    title: 'This page and the pages under it',
    description: 'Follows links that stay under this path on the same site.',
    icon: FolderTree,
  },
  { value: 'site', title: 'The whole site', description: 'Follows links anywhere on the same site.', icon: Earth },
];

function fieldErrors(error: unknown, field: string): string[] | undefined {
  return error instanceof ApiError ? error.errorsFor(field) : undefined;
}

/** One pattern per line, trimmed, without blanks or repeats. */
export function parseExcludePatterns(text: string): string[] {
  return [
    ...new Set(
      text
        .split('\n')
        .map((line) => line.trim())
        .filter(Boolean),
    ),
  ];
}

export function toAddUrlRequest(
  form: { url: string; title: string; scope: CrawlScope; maxPages: string; exclude: string },
  folderId: Guid | null,
): AddUrlSourceRequest {
  const base = { url: form.url.trim(), title: form.title.trim() || null, folderId, scope: form.scope };
  if (form.scope === 'page') {
    return base;
  }
  const maxPages = Number.parseInt(form.maxPages, 10);
  return { ...base, maxPages: Number.isFinite(maxPages) ? maxPages : DEFAULT_MAX_PAGES, exclude: parseExcludePatterns(form.exclude) };
}

function ScopeChoices({ value, onChange }: { value: CrawlScope; onChange: (scope: CrawlScope) => void }) {
  const name = useId();
  return (
    <ChoiceCardGroup label="What to add">
      {SCOPES.map((scope) => (
        <ChoiceCard
          key={scope.value}
          name={name}
          checked={value === scope.value}
          onChoose={() => onChange(scope.value)}
          icon={scope.icon}
          title={scope.title}
          titleId={`${name}-${scope.value}-title`}
          description={scope.description}
          descriptionId={`${name}-${scope.value}-description`}
        />
      ))}
    </ChoiceCardGroup>
  );
}

function Added({ source, fallback, onDismiss }: { source: SourceDto | undefined; fallback: string; onDismiss: () => void }) {
  const website = source?.type === 'website';
  return (
    <Alert tone="success" action={<Button variant="ghost" size="icon-sm" icon={X} aria-label="Dismiss" onClick={onDismiss} />}>
      Added “{source?.title || fallback}”.{' '}
      {website
        ? 'Its pages are found and indexed in the background; open it in the list to follow along.'
        : 'It is indexed in the background and shows up in the list right away.'}
    </Alert>
  );
}

/** Adds one page, or a website whose pages are crawled. */
export function WebsiteForm({ folderId }: { folderId: Guid | null }) {
  const queryClient = useQueryClient();
  const [url, setUrl] = useState('');
  const [title, setTitle] = useState('');
  const [scope, setScope] = useState<CrawlScope>('section');
  const [maxPages, setMaxPages] = useState(String(DEFAULT_MAX_PAGES));
  const [exclude, setExclude] = useState('');
  const [advancedOpen, setAdvancedOpen] = useState(false);
  const mutation = useMutation({
    mutationFn: (body: AddUrlSourceRequest) => sourcesApi.addUrlSource(body),
    onSuccess: () => {
      setUrl('');
      setTitle('');
      void queryClient.invalidateQueries({ queryKey: sourceKeys.all });
    },
    onError: (error) => {
      if (fieldErrors(error, 'exclude')?.length) {
        setAdvancedOpen(true);
      }
    },
  });

  const submit = (event: FormEvent) => {
    event.preventDefault();
    if (url.trim()) {
      mutation.mutate(toAddUrlRequest({ url, title, scope, maxPages, exclude }, folderId));
    }
  };

  const urlErrors = fieldErrors(mutation.error, 'url');
  const titleErrors = fieldErrors(mutation.error, 'title');
  const maxPagesErrors = fieldErrors(mutation.error, 'maxPages');
  const excludeErrors = fieldErrors(mutation.error, 'exclude');
  const crawls = scope !== 'page';
  const shownErrors = [urlErrors, titleErrors, maxPagesErrors, excludeErrors].some((errors) => errors?.length);
  return (
    <form onSubmit={submit} className="space-y-4">
      <Field label="Address" htmlFor="source-url" errors={urlErrors} hint="Public pages only; the readable text of each page is extracted.">
        <input
          {...fieldAria('source-url', urlErrors, true)}
          {...inputProps}
          type="url"
          inputMode="url"
          required
          placeholder="https://example.com/docs"
          value={url}
          onChange={(event) => setUrl(event.target.value)}
          className="field"
        />
      </Field>
      <ScopeChoices value={scope} onChange={setScope} />
      {crawls ? (
        <Field
          label="Max pages"
          htmlFor="source-max-pages"
          errors={maxPagesErrors}
          hint="The crawl stops after this many pages. A sitemap is used when the site has one, otherwise links are followed up to three clicks deep."
        >
          <input
            {...fieldAria('source-max-pages', maxPagesErrors, true)}
            type="number"
            inputMode="numeric"
            min={1}
            step={1}
            value={maxPages}
            onChange={(event) => setMaxPages(event.target.value)}
            className="field w-32"
          />
        </Field>
      ) : null}
      <Field label="Title" htmlFor="source-url-title" optional errors={titleErrors}>
        <input
          {...fieldAria('source-url-title', titleErrors)}
          placeholder={crawls ? 'Defaults to the address' : 'Defaults to the page title'}
          value={title}
          onChange={(event) => setTitle(event.target.value)}
          className="field"
        />
      </Field>
      {crawls ? (
        <CollapsibleSection title="Advanced" description="Leave parts of the site out." open={advancedOpen} onOpenChange={setAdvancedOpen}>
          <Field
            label="Exclude"
            htmlFor="source-exclude"
            optional
            errors={excludeErrors}
            hint="One path pattern per line, such as /blog/* or */changelog. * matches anything."
          >
            <textarea
              {...fieldAria('source-exclude', excludeErrors, true)}
              {...inputProps}
              rows={3}
              placeholder={'/blog/*\n*/print'}
              value={exclude}
              onChange={(event) => setExclude(event.target.value)}
              className="field resize-y font-mono text-xs"
            />
          </Field>
        </CollapsibleSection>
      ) : null}
      {mutation.isError && !shownErrors ? <ErrorMessage error={mutation.error} /> : null}
      {mutation.isSuccess ? (
        <Added source={mutation.data} fallback={mutation.variables?.title || mutation.variables?.url || 'page'} onDismiss={() => mutation.reset()} />
      ) : null}
      <Button type="submit" variant="primary" icon={Plus} loading={mutation.isPending} disabled={!url.trim()}>
        {crawls ? 'Add website' : 'Add page'}
      </Button>
    </form>
  );
}
