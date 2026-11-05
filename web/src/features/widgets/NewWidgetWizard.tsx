import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useEffect, useEffectEvent, useId, useMemo, useRef, useState, type FormEvent } from 'react';
import { flushSync } from 'react-dom';
import { useLocation, useNavigate, useSearchParams } from 'react-router';
import { Alert, Card, ErrorMessage } from '../../components/ui/feedback';
import { LeaveGuard, useLeaveGuard } from '../../components/ui/LeaveGuard';
import { Page } from '../../components/ui/Page';
import { Skeleton } from '../../components/ui/Skeleton';
import { toast } from '../../components/ui/Toast';
import { WizardFrame } from '../../components/ui/WizardFrame';
import { WizardProgress } from '../../components/ui/WizardProgress';
import { ApiError } from '../../lib/api';
import { useBreadcrumb } from '../../lib/breadcrumb';
import { readReturnTo } from '../../lib/returnTo';
import { useEscapeToLeave } from '../../lib/hooks/useKeydown';
import { resolveTheme } from '../../widget/shared/theme';
import { ownAgents, useAgents } from '../agents/api';
import type { AgentDto } from '../agents/types';
import { useIntegrations } from '../notifications/api';
import type { IntegrationsDto } from '../notifications/types';
import { useWidgetOptions, widgetKeys, widgetsApi } from './api';
import { PropertyField, type EditorKit } from './editor/controls';
import { readPath, useWidgetDraft, type DraftPath } from './editor/draft';
import { buildRegistry } from './editor/registry';
import type { SaveWidgetRequest, WidgetDto, WidgetOptionsDto } from './types';
import { AgentStep, ReviewStep } from './NewWidgetWizardSteps';
import {
  initialWidgetValues,
  toSaveRequest,
  validateWidget,
  WIDGET_FIELDS,
  type WidgetField,
  type WidgetFormErrors,
  type WidgetFormValues,
} from './widgetForm';
import {
  firstOpenWidgetStepBefore,
  NUMBERED_WIDGET_STEPS,
  parseWidgetStep,
  WIDGET_WIZARD_STEPS,
  widgetStepErrors,
  widgetStepIndex,
  widgetStepOfField,
  type WidgetWizardStep,
} from './widgetWizard';

// For a radio group, its first choice.
function invalidControl(form: HTMLFormElement | null): HTMLElement | null {
  return form?.querySelector<HTMLElement>('[aria-invalid="true"] input, [aria-invalid="true"]:not([role="radiogroup"])') ?? null;
}

function WizardSkeleton() {
  return (
    <Page width="narrow">
      <p role="status" className="sr-only">
        Loading
      </p>
      {Array.from({ length: 2 }, (_, index) => (
        <Skeleton key={index} shape="card" className="h-40" />
      ))}
    </Page>
  );
}

export function NewWidgetPage() {
  const options = useWidgetOptions();
  const agents = useAgents();
  const integrations = useIntegrations();
  useBreadcrumb('New widget');
  // The API rejects the built-in agent for widgets.
  const usable = useMemo(() => ownAgents(agents.data ?? []).sort((a, b) => a.name.localeCompare(b.name)), [agents.data]);

  const error = options.error ?? agents.error;
  if (error) {
    return (
      <Page width="narrow">
        <ErrorMessage error={error} title="Could not start a new widget" />
      </Page>
    );
  }
  if (!options.data || !agents.data) {
    return <WizardSkeleton />;
  }
  return <WidgetWizard options={options.data} agents={usable} integrations={integrations.data} />;
}

interface WidgetWizardProps {
  options: WidgetOptionsDto;
  agents: readonly AgentDto[];
  integrations: IntegrationsDto | undefined;
}

function WidgetWizard({ options, agents, integrations }: WidgetWizardProps) {
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const location = useLocation();
  const [search, setSearch] = useSearchParams();
  const formId = useId();
  const formRef = useRef<HTMLFormElement>(null);
  const guard = useLeaveGuard();
  const registry = useMemo(() => buildRegistry(options), [options]);
  const [entryState] = useState<unknown>(() => location.state);
  const presetAgent = (entryState as { agentId?: string } | null)?.agentId;
  const draft = useWidgetDraft(() =>
    initialWidgetValues(null, options.defaults, presetAgent && agents.some((agent) => agent.id === presetAgent) ? presetAgent : ''),
  );
  const { values, set } = draft;
  const [checked, setChecked] = useState<ReadonlySet<WidgetWizardStep>>(() => new Set());
  const [submitted, setSubmitted] = useState<WidgetFormValues | null>(null);

  const create = useMutation({
    mutationFn: (body: SaveWidgetRequest) => widgetsApi.createWidget(body),
    onSuccess: (saved) => {
      // Seed the list cache so the editor finds the new widget right away.
      queryClient.setQueryData<WidgetDto[]>(widgetKeys.list, (old) => (old ? [...old.filter((item) => item.id !== saved.id), saved] : old));
      void queryClient.invalidateQueries({ queryKey: widgetKeys.list });
      toast({ title: 'Widget created', tone: 'success' });
      guard.allowLeave();
      const returnTo = readReturnTo(entryState);
      navigate(`/widgets/${saved.id}`, {
        replace: true,
        state: { notice: 'Add the snippet to your site to show the chat.', ...(returnTo ? { returnTo } : {}) },
      });
    },
    onError: (error) => {
      if (error instanceof ApiError && error.isValidation) {
        const field = WIDGET_FIELDS.find((item) => error.errorsFor(item).length > 0);
        if (field && widgetStepOfField(field) !== 'review') {
          goTo(widgetStepOfField(field));
        }
      }
    },
  });

  const requested = parseWidgetStep(search.get('step')) ?? 'agent';
  // A deep link, or a reload that lost the values, falls back to the first open step.
  const open = firstOpenWidgetStepBefore(requested, values);
  const step = open ?? requested;
  const index = widgetStepIndex(step);
  const meta = WIDGET_WIZARD_STEPS[index]!;

  const goTo = (next: WidgetWizardStep, replace = false) =>
    setSearch(
      (current) => {
        const params = new URLSearchParams(current);
        params.set('step', next);
        return params;
      },
      { replace, state: location.state },
    );

  const replaceStep = useEffectEvent((next: WidgetWizardStep) => goTo(next, true));
  useEffect(() => {
    if (open && open !== requested) {
      replaceStep(open);
    }
  }, [open, requested]);

  useEscapeToLeave(() => navigate('/widgets'));

  const validation = create.error instanceof ApiError && create.error.isValidation ? create.error : null;
  const clientErrors = useMemo(() => {
    const all = validateWidget(values);
    return Object.fromEntries(Object.entries(all).filter(([field]) => checked.has(widgetStepOfField(field)))) as WidgetFormErrors;
  }, [values, checked]);
  const fieldValue = (from: WidgetFormValues, field: WidgetField) =>
    JSON.stringify([...registry.properties.values()].filter((property) => property.field === field).map((property) => readPath(from, property.id)));
  const errorsFor = (field: WidgetField): string[] | undefined => {
    const client = clientErrors[field];
    if (client) {
      return [client];
    }
    if (!validation || !submitted || fieldValue(values, field) !== fieldValue(submitted, field)) {
      return undefined;
    }
    const server = validation.errorsFor(field);
    return server.length > 0 ? server : undefined;
  };

  const mode = document.documentElement.classList.contains('dark') ? 'dark' : 'light';
  const kit: EditorKit = {
    values,
    saved: values,
    set,
    errorsFor,
    mode,
    theme: resolveTheme({ preset: values.preset, brandColor: values.brandColor, mode, overrides: values.colors[mode] }),
    id: (name) => `${formId}-${name}`,
    agents,
    integrations,
    widgetId: null,
  };
  const field = (id: DraftPath) => {
    const property = registry.properties.get(id);
    return property ? <PropertyField property={property} kit={kit} /> : null;
  };

  const submit = (event: FormEvent) => {
    event.preventDefault();
    if (step === 'review') {
      const { request, errors } = toSaveRequest(values, options.defaults);
      if (!request) {
        setChecked(new Set(WIDGET_WIZARD_STEPS.map((item) => item.id)));
        goTo(widgetStepOfField(Object.keys(errors)[0]!));
        return;
      }
      setSubmitted(values);
      create.mutate(request);
      return;
    }
    if (Object.keys(widgetStepErrors(step, values)).length > 0) {
      // Render the errors synchronously so the first one can take focus.
      flushSync(() => setChecked((current) => new Set(current).add(step)));
      invalidControl(formRef.current)?.focus();
      return;
    }
    goTo(WIDGET_WIZARD_STEPS[index + 1]!.id);
  };

  const noAgents = agents.length === 0;
  const otherErrors = validation ? validation.errorsExcept(WIDGET_FIELDS) : [];

  return (
    <>
      <WizardFrame
        step={step}
        progress={<WizardProgress steps={NUMBERED_WIDGET_STEPS} current={index} onGo={(target) => goTo(target)} />}
        onCancel={() => navigate('/widgets')}
        formId={formId}
        formRef={formRef}
        onSubmit={submit}
        title={meta.title}
        description={meta.why}
        invalidControl={() => invalidControl(formRef.current)}
        onBack={index > 0 ? () => goTo(WIDGET_WIZARD_STEPS[index - 1]!.id) : undefined}
        last={step === 'review'}
        createLabel="Create widget"
        creating={create.isPending}
        canSubmit={!noAgents}
      >
        {step === 'agent' ? (
          <AgentStep
            agents={agents}
            value={values.agentId}
            errors={errorsFor('agentId')}
            onChoose={(agentId) => set('agentId', agentId)}
            idPrefix={formId}
          />
        ) : null}

        {step === 'sites' ? (
          <Card className="space-y-5 p-5">
            {field('name')}
            {field('allowedOrigins')}
          </Card>
        ) : null}

        {step === 'look' ? (
          <Card className="space-y-5 p-5">
            {field('preset')}
            {field('brandColor')}
          </Card>
        ) : null}

        {step === 'person' ? (
          <Card className="space-y-5 p-5">
            {field('handoverEnabled')}
            {values.handoverEnabled ? (
              <div className="space-y-5 border-t border-border-subtle pt-4">
                {field('channel')}
                {field('emailTo')}
                {field('slackChannelId')}
              </div>
            ) : null}
          </Card>
        ) : null}

        {step === 'review' ? <ReviewStep values={values} agents={agents} onChange={(target) => goTo(target)} /> : null}

        {create.isError && !validation ? <ErrorMessage error={create.error} title="The widget was not created" /> : null}
        {otherErrors.length > 0 ? (
          <Alert tone="danger" title="The widget was not created" live>
            {otherErrors.join(' ')}
          </Alert>
        ) : null}
      </WizardFrame>

      <LeaveGuard
        when={draft.dirty && !create.isPending}
        guard={guard}
        samePlace={(current, next) => current.pathname === next.pathname}
        message="This widget is not created yet. If you leave now, what you entered is lost."
      />
    </>
  );
}
