import { useMutation, useQueryClient } from '@tanstack/react-query';
import { Save } from 'lucide-react';
import { useState, type FormEvent } from 'react';
import { Link } from 'react-router';
import { fieldAria } from '../../components/ui/aria';
import { Button } from '../../components/ui/Button';
import { Alert, ErrorMessage } from '../../components/ui/feedback';
import { Field } from '../../components/ui/Field';
import { Select } from '../../components/ui/Select';
import { toast } from '../../components/ui/Toast';
import { ApiError } from '../../lib/api';
import { modelKeys, modelsApi } from './api';
import {
  autoProviderLabel,
  defaultModelFor,
  initialFormValues,
  isEmbeddingProfile,
  profileRole,
  providerSetupHint,
  providersForKind,
  REASONING_EFFORTS,
  REASONING_OUTPUTS,
  toUpdateRequest,
  type ProfileFormErrors,
  type ProfileFormValues,
} from './profileSettings';
import { PROVIDERS_PATH } from './providerSettings';
import type { ModelProfileDto, ProviderDto, UpdateModelProfileRequest } from './types';

const EFFORT_LABELS: Record<string, string> = { low: 'Low', medium: 'Medium', high: 'High', extra_high: 'Extra high' };
const OUTPUT_LABELS: Record<string, string> = { none: 'None', summary: 'Summary', full: 'Full' };

export interface ProfileFormProps {
  profile: ModelProfileDto;
  providers: ProviderDto[];
  onDone: () => void;
}

export function ProfileForm({ profile, providers, onDone }: ProfileFormProps) {
  const queryClient = useQueryClient();
  const embedding = isEmbeddingProfile(profile);
  const role = profileRole(profile.name);
  const [values, setValues] = useState<ProfileFormValues>(() => initialFormValues(profile));
  const [clientErrors, setClientErrors] = useState<ProfileFormErrors>({});
  const id = (field: string) => `profile-${profile.name}-${field}`;

  const save = useMutation({
    mutationFn: (request: UpdateModelProfileRequest) => modelsApi.updateProfile(profile.name, request),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: modelKeys.profiles });
      toast({ title: `${role.label} saved`, tone: 'success' });
      onDone();
    },
  });

  const set = <K extends keyof ProfileFormValues>(key: K, value: ProfileFormValues[K]) => {
    setValues((current) => ({ ...current, [key]: value }));
    setClientErrors((current) => ({ ...current, [key]: undefined }));
  };

  const errorsFor = (field: keyof ProfileFormValues): string[] | undefined => {
    const client = clientErrors[field];
    if (client) {
      return [client];
    }
    return save.error instanceof ApiError ? save.error.errorsFor(field) : undefined;
  };

  const submit = (event: FormEvent) => {
    event.preventDefault();
    const { request, errors } = toUpdateRequest(values, profile.kind);
    setClientErrors(errors);
    if (request) {
      save.mutate(request);
    }
  };

  const options = providersForKind(providers, profile.kind);
  const selected = providers.find((p) => p.key === values.provider);
  const modelPlaceholder =
    values.provider === 'auto' ? 'Default model of the first configured provider' : (defaultModelFor(selected, profile.kind) ?? 'Provider default');
  const unconfigured = selected && !selected.configured ? selected : null;
  const setupHint = unconfigured ? providerSetupHint(unconfigured) : null;
  const hasFieldErrors = save.error instanceof ApiError && save.error.isValidation;
  const providerOptions = [
    { value: 'auto', label: autoProviderLabel(profile.kind) },
    ...options.map((provider) => ({
      value: provider.key,
      label: `${provider.key}${provider.type !== provider.key ? ` (${provider.type})` : ''}, ${provider.configured ? 'configured' : 'not configured'}`,
    })),
    ...(values.provider !== 'auto' && !options.some((p) => p.key === values.provider) ? [{ value: values.provider, label: `${values.provider} (unknown)` }] : []),
  ];

  return (
    <form onSubmit={submit} className="space-y-4" aria-label={`Change the ${role.label.toLowerCase()}`}>
      <Field label="Provider" htmlFor={id('provider')} errors={errorsFor('provider')}>
        <Select
          {...fieldAria(id('provider'), errorsFor('provider'))}
          value={values.provider}
          onChange={(provider) => set('provider', provider)}
          options={providerOptions}
        />
      </Field>
      <Field label="Model" htmlFor={id('model')} errors={errorsFor('model')} hint="Empty uses the provider's default model.">
        <input
          {...fieldAria(id('model'), errorsFor('model'), true)}
          value={values.model}
          onChange={(e) => set('model', e.target.value)}
          placeholder={modelPlaceholder}
          className="field font-mono text-xs"
          autoComplete="off"
          spellCheck={false}
        />
      </Field>
      <div className="grid gap-4 sm:grid-cols-2">
        {embedding ? (
          <Field label="Dimensions" htmlFor={id('dimensions')} errors={errorsFor('dimensions')} hint="Changing dimensions requires re-indexing all sources.">
            <input
              {...fieldAria(id('dimensions'), errorsFor('dimensions'), true)}
              inputMode="numeric"
              value={values.dimensions}
              onChange={(e) => set('dimensions', e.target.value)}
              placeholder="Model default"
              className="field"
            />
          </Field>
        ) : (
          <>
            <Field label="Reasoning effort" htmlFor={id('effort')} errors={errorsFor('reasoningEffort')}>
              <Select
                {...fieldAria(id('effort'), errorsFor('reasoningEffort'))}
                value={values.reasoningEffort}
                onChange={(effort) => set('reasoningEffort', effort)}
                options={[{ value: '', label: 'Default' }, ...REASONING_EFFORTS.map((effort) => ({ value: effort, label: EFFORT_LABELS[effort] ?? effort }))]}
              />
            </Field>
            <Field label="Reasoning output" htmlFor={id('output')} errors={errorsFor('reasoningOutput')} hint="Summary or full shows a Thinking block in chat.">
              <Select
                {...fieldAria(id('output'), errorsFor('reasoningOutput'), true)}
                value={values.reasoningOutput}
                onChange={(output) => set('reasoningOutput', output)}
                options={[{ value: '', label: 'Default' }, ...REASONING_OUTPUTS.map((output) => ({ value: output, label: OUTPUT_LABELS[output] ?? output }))]}
              />
            </Field>
            <Field label="Max output tokens" htmlFor={id('max')} errors={errorsFor('maxOutputTokens')}>
              <input
                {...fieldAria(id('max'), errorsFor('maxOutputTokens'))}
                inputMode="numeric"
                value={values.maxOutputTokens}
                onChange={(e) => set('maxOutputTokens', e.target.value)}
                placeholder="Provider default"
                className="field"
              />
            </Field>
            <Field
              label="Temperature"
              htmlFor={id('temperature')}
              optional
              errors={errorsFor('temperature')}
              hint="0–2. Claude models ignore it (Opus 5.5 rejects sampling parameters)."
            >
              <input
                {...fieldAria(id('temperature'), errorsFor('temperature'), true)}
                inputMode="decimal"
                value={values.temperature}
                onChange={(e) => set('temperature', e.target.value)}
                placeholder="Not sent"
                className="field"
              />
            </Field>
          </>
        )}
      </div>

      {unconfigured ? (
        <Alert tone="warning">
          <strong className="font-medium">{unconfigured.key}</strong> is not configured. Calls with this profile fail until you{' '}
          <Link to={PROVIDERS_PATH} onClick={onDone} className="font-medium underline underline-offset-2">
            {unconfigured.usesApiKey ? 'add an API key' : unconfigured.usesEndpoint ? 'set its endpoint' : 'configure it'}
          </Link>{' '}
          under Providers &amp; API keys
          {setupHint ? (
            <>
              {' '}
              (or set <code className="font-mono text-xs">{setupHint}</code>)
            </>
          ) : null}
          .
        </Alert>
      ) : null}
      {save.isError ? <ErrorMessage error={save.error} title={hasFieldErrors ? 'The override was not saved' : undefined} /> : null}

      <div className="flex items-center gap-2">
        <Button type="submit" variant="primary" icon={Save} loading={save.isPending}>
          Save
        </Button>
        <Button onClick={onDone} disabled={save.isPending}>
          Cancel
        </Button>
      </div>
    </form>
  );
}
