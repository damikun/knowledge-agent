import { useMutation, useQueryClient } from '@tanstack/react-query';
import { KeyRound, Pencil, PlugZap, RotateCcw, Save } from 'lucide-react';
import { useEffect, useId, useRef, useState } from 'react';
import { StatusLabel } from '../../components/ui/StatusDot';
import { Badge } from '../../components/ui/Badge';
import { Button } from '../../components/ui/Button';
import { EntityAvatar } from '../../components/ui/EntityCard';
import { ErrorMessage } from '../../components/ui/feedback';
import { ConfirmDialog } from '../../components/ui/Modal';
import { SideSheet } from '../../components/ui/SideSheet';
import { toast } from '../../components/ui/Toast';
import { invalidateProviderSettings, modelKeys, modelsApi } from './api';
import { ConnectionResult, ProviderSettings, testSummary } from './ProviderDetails';
import { ProviderForm } from './ProviderForm';
import {
  initialProviderFormValues,
  modelFieldFor,
  PROVIDER_DESCRIPTIONS,
  providerDomId,
  toProviderUpdateRequest,
  visibleFields,
  type ProviderFormErrors,
  type ProviderFormValues,
  type ProviderTextField,
} from './providerSettings';
import type { ProviderDto, UpdateProviderRequest } from './types';

export function ProviderCard({ provider }: { provider: ProviderDto }) {
  const queryClient = useQueryClient();
  const [draft, setDraft] = useState<ProviderFormValues | null>(null);
  const [initialFocus, setInitialFocus] = useState<ProviderTextField | null>(null);
  const [clientErrors, setClientErrors] = useState<ProviderFormErrors>({});
  const [invalidAttempts, setInvalidAttempts] = useState(0);
  const [confirmReset, setConfirmReset] = useState(false);
  // Resetting removes the focused Reset button, so focus moves to Edit.
  const [focusEditButton, setFocusEditButton] = useState(0);
  const editButtonRef = useRef<HTMLButtonElement>(null);
  const fieldRef = useRef<HTMLInputElement>(null);
  const formId = useId();
  const editing = draft !== null;
  const domId = providerDomId(provider.key);
  const headingId = `${domId}-heading`;

  const showUpdated = (updated: ProviderDto | undefined) => {
    if (updated) {
      queryClient.setQueryData<ProviderDto[]>(modelKeys.providers, (old) =>
        old?.map((p) => (p.key.toLowerCase() === updated.key.toLowerCase() ? updated : p)),
      );
    }
    void invalidateProviderSettings(queryClient);
  };

  const test = useMutation({ mutationFn: () => modelsApi.testProvider(provider.key) });

  const closeForm = () => {
    setDraft(null);
    setFocusEditButton((count) => count + 1);
  };

  const save = useMutation({
    mutationFn: (request: UpdateProviderRequest) => modelsApi.updateProvider(provider.key, request),
    // The request may carry an API key, so don't keep it in the mutation cache.
    gcTime: 0,
    onSuccess: (updated) => {
      showUpdated(updated);
      closeForm();
      test.reset();
      toast({ title: `${provider.key} saved`, tone: 'success' });
    },
  });

  const reset = useMutation({
    mutationFn: () => modelsApi.resetProvider(provider.key),
    onSuccess: (updated) => {
      setConfirmReset(false);
      showUpdated(updated);
      test.reset();
      toast({ title: 'Portal settings removed', description: 'The configured values apply again.', tone: 'success' });
      setFocusEditButton((count) => count + 1);
    },
  });

  useEffect(() => {
    if (focusEditButton > 0) {
      editButtonRef.current?.focus();
    }
  }, [focusEditButton]);

  const openForm = (patch: Partial<ProviderFormValues> = {}, focus: ProviderTextField | null = visibleFields(provider)[0] ?? null) => {
    if (!editing) {
      save.reset();
      setClientErrors({});
      setInvalidAttempts(0);
      setInitialFocus(focus);
    }
    setDraft((current) => ({ ...(current ?? initialProviderFormValues(provider)), ...patch }));
  };

  const set = <K extends keyof ProviderFormValues>(field: K, value: ProviderFormValues[K]) => {
    setDraft((current) => (current ? { ...current, [field]: value } : current));
    if (field !== 'clearApiKey') {
      setClientErrors((current) => ({ ...current, [field]: undefined }));
    }
  };

  const pickModel = (model: string) => {
    const field = modelFieldFor(provider, model);
    if (field) {
      openForm({ [field]: model }, field);
      setClientErrors((current) => ({ ...current, [field]: undefined }));
    }
  };

  const submit = () => {
    if (!draft) {
      return;
    }
    const { request, errors } = toProviderUpdateRequest(draft, provider);
    setClientErrors(errors);
    if (request) {
      save.mutate(request);
    } else {
      setInvalidAttempts((count) => count + 1);
    }
  };

  const cancel = () => {
    save.reset();
    closeForm();
  };

  const needsKey = provider.usesApiKey && provider.apiKeySource === 'none';
  const description = PROVIDER_DESCRIPTIONS[provider.type.toLowerCase()];
  const busy = save.isPending || reset.isPending;
  const offers = [provider.supportsChat && 'Chat', provider.supportsEmbeddings && 'Embeddings', provider.supportsRerank && 'Reranking'].filter(
    (offer): offer is string => Boolean(offer),
  );

  return (
    <section aria-labelledby={headingId} className="flex flex-col rounded-2xl border border-border bg-surface">
      <div className="flex items-start gap-3 p-4">
        <EntityAvatar name={provider.key} />
        <div className="min-w-0 flex-1">
          <div className="flex flex-wrap items-center justify-between gap-x-2 gap-y-1">
            <div className="flex min-w-0 items-baseline gap-1.5">
              <h3 id={headingId} className="truncate text-sm font-semibold text-fg">
                {provider.key}
              </h3>
              {provider.type !== provider.key ? <span className="shrink-0 text-xs text-fg-muted">({provider.type})</span> : null}
            </div>
            <StatusLabel tone={provider.configured ? 'success' : 'neutral'}>
              {provider.configured ? 'Ready' : 'Not set up'}
            </StatusLabel>
          </div>
          {description ? <p className="mt-0.5 text-small text-fg-secondary">{description}</p> : null}
          {offers.length > 0 ? (
            <ul aria-label="Offers" className="mt-2 flex flex-wrap gap-1">
              {offers.map((offer) => (
                <li key={offer}>
                  <Badge>{offer}</Badge>
                </li>
              ))}
            </ul>
          ) : null}
        </div>
      </div>

      <div className="flex-1 space-y-3 border-t border-border-subtle px-4 py-3">
        <ProviderSettings provider={provider} />
        {test.data ? <ConnectionResult key={test.submittedAt} provider={provider} result={test.data} disabled={save.isPending} onUseModel={pickModel} /> : null}
        {test.isError ? <ErrorMessage error={test.error} title="The connection test could not run" /> : null}
        <p className="sr-only" role="status">
          {test.data ? testSummary(provider, test.data) : ''}
        </p>
      </div>

      <div className="flex flex-wrap items-center gap-1.5 rounded-b-2xl border-t border-border-subtle bg-surface-subtle px-4 py-3">
        {/* Never disabled: focus returns here when the modal panels close. */}
        <Button ref={editButtonRef} size="sm" variant="secondary" icon={needsKey ? KeyRound : Pencil} onClick={() => openForm()}>
          {needsKey ? 'Set API key' : 'Edit'}
        </Button>
        <Button size="sm" icon={PlugZap} loading={test.isPending} disabled={busy} onClick={() => test.mutate()} title="Checks the saved settings; generates no tokens">
          Test connection
        </Button>
        {provider.hasOverride ? (
          <Button size="sm" variant="ghost" icon={RotateCcw} disabled={busy} onClick={() => setConfirmReset(true)} className="ml-auto">
            Reset to configuration
          </Button>
        ) : null}
      </div>

      <SideSheet
        open={editing}
        onClose={cancel}
        title={provider.key}
        description={description ?? 'The API key, endpoint and default models of this provider.'}
        initialFocus={fieldRef}
        footer={
          <>
            <Button onClick={cancel} disabled={save.isPending}>
              Cancel
            </Button>
            <Button type="submit" form={formId} variant="primary" icon={Save} loading={save.isPending}>
              Save
            </Button>
          </>
        }
      >
        {draft ? (
          <ProviderForm
            id={formId}
            provider={provider}
            values={draft}
            errors={clientErrors}
            saveError={save.error}
            saving={save.isPending}
            invalidAttempts={invalidAttempts}
            initialFocus={initialFocus}
            focusRef={fieldRef}
            onChange={set}
            onSubmit={submit}
          />
        ) : null}
      </SideSheet>
      <ConfirmDialog
        open={confirmReset}
        title={`Reset ${provider.key}?`}
        message={
          <>
            The settings saved in the portal
            {provider.apiKeySource === 'portal' ? <>, including the API key{provider.apiKeyHint ? ` ${provider.apiKeyHint}` : ''},</> : null} are removed
            and the values from configuration apply again.
          </>
        }
        confirmLabel="Reset"
        tone="primary"
        busy={reset.isPending}
        error={reset.isError ? <ErrorMessage error={reset.error} /> : null}
        onConfirm={() => reset.mutate()}
        onCancel={() => setConfirmReset(false)}
      />
    </section>
  );
}
