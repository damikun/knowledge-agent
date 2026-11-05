import { Globe, Save } from 'lucide-react';
import { useId, useRef, useState, type FormEvent } from 'react';
import { fieldAria } from '../../components/ui/aria';
import { Button } from '../../components/ui/Button';
import { ErrorMessage, Spinner } from '../../components/ui/feedback';
import { Field } from '../../components/ui/Field';
import { toast } from '../../components/ui/Toast';
import { ApiError } from '../../lib/api';
import { useScrollToHash } from '../../lib/hooks/useScrollToHash';
import { checkPublicAddress, usesPlaceholderOrigin } from '../../lib/publicOrigin';
import { useSystemInfo } from './api';
import { toPublicAddress, useSavePublicAddress } from './publicAddress';
import type { PublicAddressDto } from './types';

const PUBLIC_ADDRESS_ANCHOR = 'public-address';

function sourceText(address: PublicAddressDto): string {
  switch (address.source) {
    case 'saved':
      return 'Saved here.';
    case 'configuration':
      return 'From the configuration (App:PublicUrl). Saving here takes its place.';
    case 'email':
      return 'From the portal address in the email settings. Saving here takes its place.';
    default:
      return usesPlaceholderOrigin(null)
        ? 'Not set yet. Until then, the code to copy shows yourdomain.com, and emails and Slack messages have no links.'
        : 'Not set yet. Until then, the code to copy uses the address in your browser, and emails and Slack messages have no links.';
  }
}

export function PublicAddressSection() {
  const info = useSystemInfo();
  const save = useSavePublicAddress();
  const headingId = useId();
  const inputId = useId();
  const inputRef = useRef<HTMLInputElement>(null);
  const [draft, setDraft] = useState<string | null>(null);
  const [clientError, setClientError] = useState<string | null>(null);
  const loaded = info.isSuccess;
  const address = toPublicAddress(info.data?.publicAddress);
  const value = draft ?? address.url ?? '';

  // Opened from a "Set your public address" link: scroll to the field and focus it.
  useScrollToHash({ enabled: loaded, block: 'center', resolve: (hash) => (hash === PUBLIC_ADDRESS_ANCHOR ? inputRef.current : null) });

  const serverErrors = save.error instanceof ApiError && save.error.isValidation ? save.error.errorsFor('url') : [];
  const errors = clientError ? [clientError] : serverErrors;

  const submit = (event: FormEvent) => {
    event.preventDefault();
    const checked = checkPublicAddress(value);
    if (checked.error) {
      setClientError(checked.error);
      return;
    }
    save.mutate(checked.url, {
      onSuccess: (saved) => {
        setDraft(null);
        toast({ title: saved.source === 'saved' ? 'Public address saved' : 'Public address removed', tone: 'success' });
      },
      onError: (error) => {
        if (!(error instanceof ApiError && error.isValidation)) {
          toast({ title: 'The public address could not be saved', tone: 'danger' });
        }
      },
    });
  };

  return (
    <section id={PUBLIC_ADDRESS_ANCHOR} aria-labelledby={headingId} className="space-y-3">
      <div>
        <h2 id={headingId} className="text-section text-fg">
          Public address
        </h2>
        <p className="mt-0.5 text-sm text-fg-secondary">Used in embed code, links in emails and Slack messages, and the MCP and API addresses.</p>
      </div>
      <div className="flex items-start gap-3 rounded-2xl border border-border bg-surface p-4">
        <span aria-hidden className="flex size-10 shrink-0 items-center justify-center rounded-lg bg-fill-muted text-fg-muted">
          <Globe className="size-5" />
        </span>
        <div className="min-w-0 flex-1">
          {info.isPending ? (
            <Spinner label="Loading the public address" />
          ) : info.isError ? (
            <ErrorMessage error={info.error} title="Could not load the public address" />
          ) : (
            <form onSubmit={submit} noValidate aria-label="Public address" className="flex flex-col gap-3 sm:flex-row sm:items-start">
              <Field
                label="Public address of this installation"
                htmlFor={inputId}
                errors={errors}
                hint={sourceText(address)}
                className="min-w-0 flex-1"
              >
                <input
                  ref={inputRef}
                  {...fieldAria(inputId, errors, true)}
                  type="url"
                  inputMode="url"
                  autoComplete="off"
                  spellCheck={false}
                  value={value}
                  placeholder="https://support.acme.com"
                  onChange={(event) => {
                    setDraft(event.target.value);
                    setClientError(null);
                    save.reset();
                  }}
                  className="field"
                />
              </Field>
              <Button type="submit" variant="primary" icon={Save} loading={save.isPending} disabled={draft === null} className="sm:mt-6">
                Save
              </Button>
            </form>
          )}
        </div>
      </div>
    </section>
  );
}
