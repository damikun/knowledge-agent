import { useMutation, useQueryClient } from '@tanstack/react-query';
import { clsx } from 'clsx';
import { Save } from 'lucide-react';
import { useMemo, type FormEvent } from 'react';
import { Link } from 'react-router';
import { Button } from '../../components/ui/Button';
import { Alert, ErrorMessage } from '../../components/ui/feedback';
import { fieldAria } from '../../components/ui/aria';
import { Field } from '../../components/ui/Field';
import { Select } from '../../components/ui/Select';
import { StatusDot } from '../../components/ui/StatusDot';
import { toast } from '../../components/ui/Toast';
import { ApiError } from '../../lib/api';
import { profileRole } from '../models/profileSettings';
import { useSourceFolders } from '../sources/api';
import { pickableFolderIds } from '../sources/folders';
import type { WidgetDto } from '../widgets/types';
import {
  FieldGroup,
  GeneralKnowledgeField,
  HandoverField,
  InstructionsField,
  KnowledgeFields,
  LanguageField,
  NameFields,
  SourcesNotice,
  ToneField,
  ToolsField,
  WidgetHandover,
  type FieldKit,
} from './agentFields';
import {
  initialAgentValues,
  sameFields,
  SECTION_FIELDS,
  toSaveRequest,
  withSection,
  type AgentFormField,
  type AgentFormValues,
  type AgentSection,
} from './agentForm';
import { agentKeys, agentsApi, useAgents } from './api';
import { LeaveGuard, type LeaveGuardHandle } from '../../components/ui/LeaveGuard';
import { McpAppsField } from './McpAppsField';
import { SpecialistsField } from './SpecialistsField';
import type { AgentDto, AgentOptionsDto, SaveAgentRequest } from './types';
import { useAgentForm } from './useAgentForm';
import { TABS } from './agentTabs';
import { useReturnState } from '../../lib/returnTo';

const NOT_SAVED = 'Your changes were not saved';

export interface SectionFormProps {
  agent: AgentDto;
  options: AgentOptionsDto;
  section: AgentSection;
  using: readonly WidgetDto[] | undefined;
  guard: LeaveGuardHandle;
}

/** Saves only its own fields on top of the saved agent, so one tab never carries another tab's edits. */
export function SectionForm({ agent, options, section, using, guard }: SectionFormProps) {
  const queryClient = useQueryClient();
  const folders = useSourceFolders();
  const saved = useMemo(() => initialAgentValues(agent, options), [agent, options]);
  const fields = SECTION_FIELDS[section];
  const shownFields = fields.filter(
    (field): field is AgentFormField =>
      field !== 'scope' && field !== 'allowGeneralKnowledge' && field !== 'handoverPolicy' && field !== 'showSpecialistName',
  );

  const save = useMutation({
    mutationFn: (body: SaveAgentRequest) => agentsApi.updateAgent(agent.id, body),
    onSuccess: (result) => {
      if (result) {
        queryClient.setQueryData<AgentDto[]>(agentKeys.list, (old) => old?.map((item) => (item.id === result.id ? result : item)));
      }
      void queryClient.invalidateQueries({ queryKey: agentKeys.list });
      toast({ title: 'Agent saved', tone: 'success' });
    },
  });
  const form = useAgentForm(saved, { isNew: false, serverError: save.error });
  const { values, kit } = form;
  const dirty = !sameFields(values, saved, fields);

  const submit = (event: FormEvent) => {
    event.preventDefault();
    const knownFolderIds = folders.data ? pickableFolderIds(folders.data.folders) : undefined;
    const { request, errors } = toSaveRequest(withSection(saved, values, section), { isNew: false, options, knownFolderIds });
    form.setClientErrors(errors);
    if (request) {
      save.mutate(request);
    } else {
      form.focusInvalid();
    }
  };

  const discard = () => {
    form.setValues({ ...values, ...Object.fromEntries(fields.map((field) => [field, saved[field]])) } as AgentFormValues);
    form.setClientErrors({});
    save.reset();
  };

  const otherErrors = form.otherErrors(shownFields);
  const clientOther = Object.entries(form.clientErrors)
    .filter(([field]) => !shownFields.includes(field as AgentFormField))
    .map(([, message]) => message);

  return (
    <form ref={form.formRef} onSubmit={submit} noValidate aria-label={TABS.find((tab) => tab.id === section)!.label} className="@container max-w-3xl space-y-6">
      <SectionFields section={section} kit={kit} agent={agent} options={options} using={using} />

      {save.isError && !(save.error instanceof ApiError && save.error.isValidation) ? (
        <ErrorMessage error={save.error} title={NOT_SAVED} />
      ) : null}
      {otherErrors.length + clientOther.length > 0 ? (
        <Alert tone="danger" title={NOT_SAVED} live>
          {[...clientOther, ...otherErrors].join(' ')}
        </Alert>
      ) : null}

      {dirty || save.isPending ? (
        <div className="sticky bottom-0 z-10 -mx-1 flex flex-wrap items-center justify-end gap-2 rounded-xl border border-border bg-raised px-4 py-3 shadow-menu">
          <p role="status" className="mr-auto flex items-center gap-2 text-small text-fg-secondary">
            <StatusDot tone="warning" />
            Unsaved changes
          </p>
          <Button variant="ghost" onClick={discard} disabled={save.isPending}>
            Cancel
          </Button>
          <Button type="submit" variant="primary" icon={Save} loading={save.isPending}>
            Save changes
          </Button>
        </div>
      ) : null}

      <LeaveGuard when={dirty && !save.isPending} guard={guard} />
    </form>
  );
}

function SectionFields({
  section,
  kit,
  agent,
  options,
  using,
}: {
  section: AgentSection;
  kit: FieldKit;
  agent: AgentDto;
  options: AgentOptionsDto;
  using: readonly WidgetDto[] | undefined;
}) {
  const id = kit.id;
  const agents = useAgents().data;
  if (section === 'general') {
    return (
      <>
        <FieldGroup id={id('identity')} title="Name and purpose" why="How you and your team recognize it. The purpose shows on its card.">
          <NameFields {...kit} isNew={false} />
        </FieldGroup>
        <FieldGroup id={id('language')} title="Language" why="The language it answers in. Most agents answer in the language each visitor writes in.">
          <LanguageField {...kit} />
        </FieldGroup>
      </>
    );
  }
  if (section === 'behavior') {
    return (
      <FieldGroup id={id('voice')} title="How it talks" why="This is how your brand sounds in every answer. Write it the way you would brief a new colleague.">
        <ToneField {...kit} ownHint="Describe the tone yourself in the instructions below." />
        <InstructionsField {...kit} />
      </FieldGroup>
    );
  }
  if (section === 'knowledge') {
    return (
      <FieldGroup id={id('knowledge')} title="What it knows" why="It answers only from what you allow here, so visitors get your facts instead of guesses.">
        <SourcesNotice />
        <KnowledgeFields {...kit} />
        <div className="border-t border-border-subtle pt-4">
          <GeneralKnowledgeField {...kit} />
        </div>
      </FieldGroup>
    );
  }
  if (section === 'tools') {
    return (
      <>
        <FieldGroup id={id('tools')} title="Built-in abilities" why="What it can do while it answers. It decides on its own when to use each one.">
          <ToolsField {...kit} available={options.tools} />
        </FieldGroup>
        <FieldGroup
          id={id('apps')}
          title="Connected apps"
          why="Apps connected on the Tools page, such as your shop or CRM. Turn one on and the agent may use all of its tools: the ones that only look things up run on their own, the ones that make changes ask first."
        >
          <McpAppsField {...kit} />
        </FieldGroup>
      </>
    );
  }
  if (section === 'handover') {
    return (
      <FieldGroup id={id('person')} title="When to offer a person" why="Some questions need a human. Decide when it offers to bring in your team.">
        <HandoverField {...kit} />
        <WidgetHandover using={using} policy={kit.values.handoverPolicy} headingId={id('used-by')} />
      </FieldGroup>
    );
  }
  if (section === 'specialists') {
    return (
      <FieldGroup
        id={id('specialists')}
        title="Specialists"
        why="Other agents it hands a conversation to, such as Billing or Orders. The specialist answers right away with its own instructions, knowledge and tools, and hands back when the topic changes."
      >
        <SpecialistsField kit={kit} agent={agent} agents={agents ?? []} />
      </FieldGroup>
    );
  }
  return <AdvancedFields kit={kit} agent={agent} options={options} />;
}

function AdvancedFields({ kit, agent, options }: { kit: FieldKit; agent: AgentDto; options: AgentOptionsDto }) {
  const returnState = useReturnState();
  const { values, set, errorsFor, id } = kit;
  const profiles = options.profiles.includes(values.profileName) ? options.profiles : [...options.profiles, values.profileName];
  return (
    <>
      <FieldGroup id={id('answers')} title="Model" why="Settings most people never change.">
        <div className={clsx('grid gap-5 @min-[36rem]:grid-cols-2')}>
          <Field
            label="Model"
            htmlFor={id('profile')}
            errors={errorsFor('profileName')}
            hint={
              <>
                The model behind each choice is set on the{' '}
                <Link to="/models" state={returnState} className="font-medium text-accent-text hover:underline">
                  Models
                </Link>{' '}
                page.
              </>
            }
          >
            <Select
              {...fieldAria(id('profile'), errorsFor('profileName'), true)}
              value={values.profileName}
              onChange={(profile) => set('profileName', profile, ['profileName'])}
              options={profiles.map((profile) => ({
                value: profile,
                label: `${profileRole(profile).label} (${profile})${options.profiles.includes(profile) ? '' : ', unknown'}`,
              }))}
            />
          </Field>
          <div className="space-y-1.5">
            <p className="text-small font-medium text-fg">Key</p>
            <p className="font-mono text-xs leading-8 text-fg-secondary">{agent.key}</p>
          </div>
        </div>
      </FieldGroup>
      <FieldGroup id={id('prompt')} title="Prompt around your instructions">
        <p className="text-small text-fg-secondary">
          Your instructions are placed inside the{' '}
          <Link to="/prompts/orchestrator-system" state={returnState} className="font-medium text-accent-text hover:underline">
            Answering agent
          </Link>{' '}
          prompt, which tells every agent how to search, cite and stay on topic.{' '}
          <span className="font-mono text-xs text-fg-muted">orchestrator-system</span>
        </p>
      </FieldGroup>
    </>
  );
}
