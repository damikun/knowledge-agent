import { useMutation, useQueryClient } from '@tanstack/react-query';
import { Bot } from 'lucide-react';
import { useEffect, useEffectEvent, useId, useMemo, useState, type FormEvent } from 'react';
import { Link, useNavigate, useSearchParams } from 'react-router';
import { buttonClass } from '../../components/ui/Button';
import { Alert, Card, EmptyState, ErrorMessage } from '../../components/ui/feedback';
import { LeaveGuard, useLeaveGuard } from '../../components/ui/LeaveGuard';
import { Page } from '../../components/ui/Page';
import { Skeleton } from '../../components/ui/Skeleton';
import { toast } from '../../components/ui/Toast';
import { WizardFrame } from '../../components/ui/WizardFrame';
import { WizardProgress } from '../../components/ui/WizardProgress';
import { ApiError } from '../../lib/api';
import { useBreadcrumb } from '../../lib/breadcrumb';
import { useEscapeToLeave } from '../../lib/hooks/useKeydown';
import { useSourceFolders } from '../sources/api';
import { buildFolderTree, pickableFolderIds } from '../sources/folders';
import {
  GeneralKnowledgeField,
  HandoverField,
  InstructionsField,
  KnowledgeFields,
  LanguageField,
  NameFields,
  SourcesNotice,
  ToneField,
  ToolsField,
  FieldGroup,
} from './agentFields';
import {
  AGENT_FORM_FIELDS,
  copyValues,
  keyFromName,
  sameFields,
  templateValues,
  toSaveRequest,
  type AgentFormErrors,
  type AgentFormValues,
  type SaveAgentContext,
} from './agentForm';
import { McpAppsField } from './McpAppsField';
import { findTemplate, type AgentTemplate } from './agentTemplates';
import { firstOpenStepBefore, NUMBERED_STEPS, parseStep, stepErrors, stepIndex, stepOfField, WIZARD_FIELDS, WIZARD_STEPS, type WizardStep } from './agentWizard';
import { agentKeys, agentsApi, useAgentOptions, useAgents } from './api';
import { ReviewStep, StartStep } from './NewAgentWizardSteps';
import type { AgentDto, AgentOptionsDto } from './types';
import { useAgentForm } from './useAgentForm';

// Survives a reload; dropped once the agent is created or the wizard is left.
const DRAFT_KEY = 'ka.new-agent-draft';
const ALL_FIELDS = Object.keys(templateValues(undefined, { tools: [], profiles: [] })).filter((field) => field !== 'keyEdited') as Array<keyof AgentFormValues>;

function readDraft(origin: string): Partial<AgentFormValues> | null {
  try {
    const parsed = JSON.parse(sessionStorage.getItem(DRAFT_KEY) ?? 'null') as { origin?: string; values?: Partial<AgentFormValues> } | null;
    return parsed?.origin === origin && parsed.values ? parsed.values : null;
  } catch {
    return null;
  }
}

function writeDraft(origin: string, values: AgentFormValues | null) {
  try {
    if (values) {
      sessionStorage.setItem(DRAFT_KEY, JSON.stringify({ origin, values }));
    } else {
      sessionStorage.removeItem(DRAFT_KEY);
    }
  } catch {
    // Storage may be blocked; the wizard works without it.
  }
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

/** ?template= or ?from={id} picks the starting point and ?step= the step, so reload and back work. */
export function NewAgentPage() {
  const [search] = useSearchParams();
  const template = findTemplate(search.get('template'));
  const fromId = template ? null : search.get('from');
  const agents = useAgents();
  const options = useAgentOptions();
  useBreadcrumb('New agent');
  const copyOf = fromId ? agents.data?.find((item) => !item.isBuiltIn && item.id.toLowerCase() === fromId.toLowerCase()) : undefined;

  const error = options.error ?? (fromId ? agents.error : null);
  if (error) {
    return (
      <Page width="narrow">
        <ErrorMessage error={error} title="Could not start a new agent" />
      </Page>
    );
  }
  if (!options.data || (fromId && !agents.data)) {
    return <WizardSkeleton />;
  }
  if (fromId && !copyOf) {
    return (
      <Page width="narrow">
        <Card>
          <EmptyState
            icon={Bot}
            title="The agent to copy does not exist"
            description="It may have been deleted. Start from a template instead."
            action={
              <Link to="/agents/new" className={buttonClass()}>
                Choose a starting point
              </Link>
            }
          />
        </Card>
      </Page>
    );
  }
  const origin = template ? `template:${template.id}` : copyOf ? `from:${copyOf.id}` : '';
  return <AgentWizard key={origin} origin={origin} options={options.data} template={template} copyOf={copyOf} />;
}

interface AgentWizardProps {
  /** "template:support", "from:{id}", or empty before a starting point is chosen. */
  origin: string;
  options: AgentOptionsDto;
  template?: AgentTemplate;
  copyOf?: AgentDto;
}

function AgentWizard({ origin, options, template, copyOf }: AgentWizardProps) {
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const [search, setSearch] = useSearchParams();
  const folders = useSourceFolders();
  const tree = useMemo(() => (folders.data ? buildFolderTree(folders.data.folders) : undefined), [folders.data]);
  const formId = useId();
  const guard = useLeaveGuard();
  const hasStart = origin !== '';
  const [initial] = useState(() => (copyOf ? copyValues(copyOf, options) : templateValues(template, options)));
  const [startError, setStartError] = useState<string | null>(null);

  const create = useMutation({
    mutationFn: (body: Parameters<typeof agentsApi.createAgent>[0]) => agentsApi.createAgent(body),
    onSuccess: (result) => {
      // The list is updated first, so the new agent's page finds it right away.
      if (result) {
        queryClient.setQueryData<AgentDto[]>(agentKeys.list, (old) => (old ? [...old.filter((item) => item.id !== result.id), result] : old));
      }
      void queryClient.invalidateQueries({ queryKey: agentKeys.list });
      toast({ title: 'Agent created', tone: 'success' });
      writeDraft(origin, null);
      guard.allowLeave();
      navigate(result ? `/agents/${result.id}` : '/agents', { replace: true, state: { created: true } });
    },
    onError: (error) => {
      // Back to the step that shows the first field the API refused.
      if (error instanceof ApiError && error.isValidation) {
        const field = AGENT_FORM_FIELDS.find((item) => error.errorsFor(item).length > 0);
        if (field && stepOfField(field) !== 'review') {
          goTo(stepOfField(field));
        }
      }
    },
  });

  const [start] = useState(() => {
    const draft: AgentFormValues = { ...initial, ...(hasStart ? readDraft(origin) : null) };
    return draft.keyEdited ? draft : { ...draft, key: keyFromName(draft.name) };
  });
  const form = useAgentForm(start, { isNew: true, serverError: create.error });
  const { values, kit } = form;
  const context: SaveAgentContext = { isNew: true, options, knownFolderIds: folders.data ? pickableFolderIds(folders.data.folders) : undefined };

  const requested = parseStep(search.get('step')) ?? (hasStart ? 'general' : 'start');
  // After a reload that lost what was typed, or a link straight to a later step, the first step left to do opens.
  const open = firstOpenStepBefore(requested, values, hasStart, context);
  const step = open ?? requested;
  const index = stepIndex(step);
  const meta = WIZARD_STEPS[index]!;

  const goTo = (next: WizardStep, replace = false) =>
    setSearch(
      (current) => {
        const params = new URLSearchParams(current);
        params.set('step', next);
        return params;
      },
      { replace },
    );

  // The address follows the step that opened instead, so Back and a reload agree with what shows.
  const replaceStep = useEffectEvent((next: WizardStep) => goTo(next, true));
  useEffect(() => {
    if (open && open !== requested) {
      replaceStep(open);
    }
  }, [open, requested]);

  useEffect(() => {
    if (hasStart) {
      writeDraft(origin, values);
    }
  }, [hasStart, origin, values]);
  // Leaving the wizard drops the draft; a reload never unmounts, so it keeps it.
  useEffect(() => () => writeDraft(origin, null), [origin]);

  // The step a page opens on puts the cursor in its first field; later steps move focus to their title (WizardFrame).
  const [openedOn] = useState(step);

  const cancel = () => navigate('/agents');
  useEscapeToLeave(cancel);

  const chooseStart = (next: { template?: string; from?: string }) => {
    setStartError(null);
    const params = new URLSearchParams({ ...next, step: 'start' });
    setSearch(params, { replace: true });
  };

  const submit = (event: FormEvent) => {
    event.preventDefault();
    if (step === 'review') {
      const { request, errors } = toSaveRequest(values, context);
      if (!request) {
        form.setClientErrors(errors);
        goTo(stepOfField(Object.keys(errors)[0]!));
        form.focusInvalid();
        return;
      }
      create.mutate(request);
      return;
    }
    const errors = stepErrors(step, values, hasStart, context);
    if (Object.keys(errors).length > 0) {
      const { start, ...fieldErrors } = errors;
      setStartError(start ?? null);
      form.setClientErrors(fieldErrors as AgentFormErrors);
      form.focusInvalid();
      return;
    }
    goTo(WIZARD_STEPS[index + 1]!.id);
  };

  const dirty = hasStart && !sameFields(values, initial, ALL_FIELDS);
  const otherErrors = form.otherErrors(WIZARD_FIELDS);

  return (
    <>
      <WizardFrame
        step={step}
        progress={<WizardProgress steps={NUMBERED_STEPS} current={index} onGo={(target) => goTo(target)} />}
        onCancel={cancel}
        formId={formId}
        formRef={form.formRef}
        onSubmit={submit}
        title={meta.title}
        description={
          <>
            {meta.why}
            {step !== 'start' && (copyOf || template) ? (
              <> It starts {copyOf ? <>as a copy of {copyOf.name}</> : <>from {template!.title}</>}.</>
            ) : null}
          </>
        }
        invalidControl={() => form.formRef.current?.querySelector<HTMLElement>('[aria-invalid="true"]:not([role="radiogroup"])')}
        onBack={index > 0 ? () => goTo(WIZARD_STEPS[index - 1]!.id) : undefined}
        last={step === 'review'}
        createLabel="Create agent"
        creating={create.isPending}
      >
        {step === 'start' ? <StartStep template={template} copyOf={copyOf} error={startError} onChoose={chooseStart} idPrefix={formId} /> : null}

        {step === 'general' ? (
          <Card className="space-y-5 p-5">
            <NameFields {...kit} isNew autoFocus={openedOn === 'general'} />
            <LanguageField {...kit} />
          </Card>
        ) : null}

        {step === 'behavior' ? (
          <Card className="space-y-5 p-5">
            <ToneField {...kit} ownHint="Describe the tone yourself in the instructions below." />
            <InstructionsField {...kit} />
          </Card>
        ) : null}

        {step === 'knowledge' ? (
          <Card className="space-y-5 p-5">
            <SourcesNotice />
            <KnowledgeFields {...kit} />
            <div className="border-t border-border-subtle pt-4">
              <GeneralKnowledgeField {...kit} />
            </div>
          </Card>
        ) : null}

        {step === 'tools' ? (
          <>
            <FieldGroup
              id={`${formId}-tools`}
              title="Built-in abilities"
              why="What it can do while it answers. It decides on its own when to use each one."
            >
              <ToolsField {...kit} available={options.tools} />
            </FieldGroup>
            <FieldGroup
              id={`${formId}-apps`}
              title="Connected apps"
              why="Apps connected on the Tools page, such as your shop or CRM. Turn one on and the agent may use its tools: the ones that make changes ask first."
            >
              <McpAppsField {...kit} />
            </FieldGroup>
            <FieldGroup id={`${formId}-person`} title="When to offer a person" why="Some questions need a human. Decide when it offers to bring in your team.">
              <HandoverField {...kit} />
            </FieldGroup>
          </>
        ) : null}

        {step === 'review' ? <ReviewStep values={values} tree={tree} options={options} onChange={(target) => goTo(target)} /> : null}

        {create.isError && !(create.error instanceof ApiError && create.error.isValidation) ? (
          <ErrorMessage error={create.error} title="The agent was not created" />
        ) : null}
        {otherErrors.length > 0 ? (
          <Alert tone="danger" title="The agent was not created" live>
            {otherErrors.join(' ')}
          </Alert>
        ) : null}
      </WizardFrame>

      <LeaveGuard
        when={dirty && !create.isPending}
        guard={guard}
        samePlace={(current, next) => current.pathname === next.pathname}
        message="This agent is not created yet. If you leave now, what you entered is lost."
      />
    </>
  );
}
