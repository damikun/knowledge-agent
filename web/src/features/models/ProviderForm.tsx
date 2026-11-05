import { Eye, EyeOff, Trash2, Undo2 } from 'lucide-react';
import { useState, type FormEvent, type RefObject } from 'react';
import { fieldAria } from '../../components/ui/aria';
import { Button } from '../../components/ui/Button';
import { Alert, ErrorMessage } from '../../components/ui/feedback';
import { Field, inputProps } from '../../components/ui/Field';
import { ApiError } from '../../lib/api';
import {
  ENDPOINT_EXAMPLE,
  hasApiKey,
  hasConfiguredEndpoint,
  isEndpointMandatory,
  providerDomId,
  visibleFields,
  type ProviderFormErrors,
  type ProviderFormValues,
  type ProviderTextField,
} from './providerSettings';
import type { ProviderDto } from './types';
import { useFocusFirstInvalid } from '../../lib/hooks/useFocusFirstInvalid';

export interface ProviderFormProps {
  /** The sheet's footer submits the form through this id. */
  id: string;
  provider: ProviderDto;
  values: ProviderFormValues;
  errors: ProviderFormErrors;
  saveError: unknown;
  saving: boolean;
  /** Bumped on a failed client-side validation, which moves focus to the first invalid field. */
  invalidAttempts: number;
  initialFocus: ProviderTextField | null;
  /** Set on the `initialFocus` input, for the sheet to focus on open. */
  focusRef: RefObject<HTMLInputElement | null>;
  onChange: <K extends keyof ProviderFormValues>(field: K, value: ProviderFormValues[K]) => void;
  onSubmit: () => void;
}

export function ProviderForm({
  id: formId,
  provider,
  values,
  errors,
  saveError,
  saving,
  invalidAttempts,
  initialFocus,
  focusRef,
  onChange,
  onSubmit,
}: ProviderFormProps) {
  const [showKey, setShowKey] = useState(false);
  const id = (field: ProviderTextField) => `${providerDomId(provider.key)}-${field}`;
  const focusTarget = (field: ProviderTextField) => (initialFocus === field ? focusRef : undefined);
  const validation = saveError instanceof ApiError && saveError.isValidation ? saveError : null;
  const otherErrors = validation ? validation.errorsExcept(visibleFields(provider)) : [];

  const errorsFor = (field: ProviderTextField): string[] | undefined => {
    const client = errors[field];
    if (client) {
      return [client];
    }
    return validation ? validation.errorsFor(field) : undefined;
  };

  const formRef = useFocusFirstInvalid(invalidAttempts, validation);

  const submit = (event: FormEvent) => {
    event.preventDefault();
    onSubmit();
  };

  const toggleClearApiKey = () => {
    if (!values.clearApiKey) {
      onChange('apiKey', '');
      setShowKey(false);
    }
    onChange('clearApiKey', !values.clearApiKey);
  };

  // Without portal settings the inputs start empty, so the placeholder shows the configured value.
  const configuredPlaceholder = (value: string | number | null, fallback = 'Configured default') =>
    !provider.hasOverride && value !== null && value !== '' ? String(value) : fallback;

  const keyExists = hasApiKey(provider);
  const hint = provider.apiKeyHint ? ` ${provider.apiKeyHint}` : '';
  const apiKeyHint = values.clearApiKey
    ? `The saved key${hint} is removed when you save. A key from configuration applies again, if there is one.`
    : keyExists
      ? `Leave empty to keep the current key${provider.apiKeyHint ? ` (${provider.apiKeyHint})` : ''}.`
      : 'Stored encrypted and never shown again.';

  const endpointMandatory = isEndpointMandatory(provider);
  const endpointHint = endpointMandatory
    ? `Required, e.g. ${ENDPOINT_EXAMPLE}.`
    : hasConfiguredEndpoint(provider)
      ? 'Empty uses the configured endpoint.'
      : 'Optional: a proxy or compatible server. Leave empty for the default.';

  return (
    <form ref={formRef} id={formId} onSubmit={submit} noValidate className="space-y-4" aria-label={`Edit ${provider.key} provider`}>
      <div className="space-y-4">
        {provider.usesApiKey ? (
          <div className="space-y-1.5">
            <Field label="API key" htmlFor={id('apiKey')} errors={errorsFor('apiKey')} hint={apiKeyHint}>
              <div className="relative">
                <input
                  {...fieldAria(id('apiKey'), errorsFor('apiKey'), true)}
                  {...inputProps}
                  type={showKey ? 'text' : 'password'}
                  value={values.apiKey}
                  onChange={(event) => onChange('apiKey', event.target.value)}
                  placeholder={values.clearApiKey ? 'The saved key will be removed' : keyExists ? 'Paste a new key to replace it' : 'Paste your API key'}
                  disabled={values.clearApiKey}
                  ref={focusTarget('apiKey')}
                  data-1p-ignore="true"
                  data-lpignore="true"
                  className="field pr-9 font-mono text-xs"
                />
                <Button
                  variant="ghost"
                  size="icon-sm"
                  icon={showKey ? EyeOff : Eye}
                  aria-label={showKey ? 'Hide API key' : 'Show API key'}
                  aria-controls={id('apiKey')}
                  disabled={values.clearApiKey}
                  onClick={() => setShowKey((visible) => !visible)}
                  className="absolute top-1/2 right-1 -translate-y-1/2"
                />
              </div>
            </Field>
            {provider.apiKeySource === 'portal' ? (
              <Button size="sm" variant="ghost" icon={values.clearApiKey ? Undo2 : Trash2} onClick={toggleClearApiKey} disabled={saving}>
                {values.clearApiKey ? 'Keep saved key' : 'Remove saved key'}
              </Button>
            ) : null}
          </div>
        ) : null}

        {provider.usesEndpoint ? (
          <Field label="Endpoint" htmlFor={id('endpoint')} errors={errorsFor('endpoint')} hint={endpointHint}>
            <input
              {...fieldAria(id('endpoint'), errorsFor('endpoint'), true)}
              {...inputProps}
              type="url"
              inputMode="url"
              required={endpointMandatory}
              value={values.endpoint}
              onChange={(event) => onChange('endpoint', event.target.value)}
              placeholder={configuredPlaceholder(provider.endpoint, endpointMandatory ? ENDPOINT_EXAMPLE : 'Provider default')}
              ref={focusTarget('endpoint')}
              className="field font-mono text-xs"
            />
          </Field>
        ) : null}

        {provider.supportsChat ? (
          <Field label="Default chat model" htmlFor={id('defaultChatModel')} errors={errorsFor('defaultChatModel')} hint="Used by chat profiles that do not name a model.">
            <input
              {...fieldAria(id('defaultChatModel'), errorsFor('defaultChatModel'), true)}
              {...inputProps}
              value={values.defaultChatModel}
              onChange={(event) => onChange('defaultChatModel', event.target.value)}
              placeholder={configuredPlaceholder(provider.defaultChatModel)}
              ref={focusTarget('defaultChatModel')}
              className="field font-mono text-xs"
            />
          </Field>
        ) : null}

        {provider.supportsEmbeddings ? (
          <>
            <Field
              label="Default embedding model"
              htmlFor={id('defaultEmbeddingModel')}
              errors={errorsFor('defaultEmbeddingModel')}
              hint="Used when the embedding profile does not name a model."
            >
              <input
                {...fieldAria(id('defaultEmbeddingModel'), errorsFor('defaultEmbeddingModel'), true)}
                {...inputProps}
                value={values.defaultEmbeddingModel}
                onChange={(event) => onChange('defaultEmbeddingModel', event.target.value)}
                placeholder={configuredPlaceholder(provider.defaultEmbeddingModel)}
                ref={focusTarget('defaultEmbeddingModel')}
                className="field font-mono text-xs"
              />
            </Field>
            <Field
              label="Embedding dimensions"
              htmlFor={id('defaultEmbeddingDimensions')}
              errors={errorsFor('defaultEmbeddingDimensions')}
              hint="Changing the embedding model or dimensions requires re-indexing all sources."
            >
              <input
                {...fieldAria(id('defaultEmbeddingDimensions'), errorsFor('defaultEmbeddingDimensions'), true)}
                inputMode="numeric"
                autoComplete="off"
                value={values.defaultEmbeddingDimensions}
                onChange={(event) => onChange('defaultEmbeddingDimensions', event.target.value)}
                placeholder={configuredPlaceholder(provider.defaultEmbeddingDimensions, 'Model default')}
                ref={focusTarget('defaultEmbeddingDimensions')}
                className="field"
              />
            </Field>
          </>
        ) : null}
      </div>

      {saveError && !validation ? <ErrorMessage error={saveError} title="The settings were not saved" /> : null}
      {otherErrors.length > 0 ? (
        <Alert tone="danger" title="The settings were not saved" live>
          {otherErrors.join(' ')}
        </Alert>
      ) : null}

    </form>
  );
}
