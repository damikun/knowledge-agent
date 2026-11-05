import { pluralize } from '../../lib/format';
import type { Guid } from '../../lib/types';
import { findNode, folderPath, folderPathLabel, subtreeIds, type FolderNode } from '../sources/folders';
import { toolInfo, type AgentTemplate } from './agentTemplates';
import type { AgentDto, AgentMcpAppDto, AgentOptionsDto, AgentSpecialistDto, HandoverPolicy, SaveAgentRequest } from './types';

// Limits of the API.
export const MAX_NAME_LENGTH = 100;
export const MAX_KEY_LENGTH = 64;
export const MAX_DESCRIPTION_LENGTH = 500;
export const MAX_INSTRUCTIONS_LENGTH = 8_000;
export const MAX_SPECIALISTS = 10;
export const MAX_WHEN_TO_USE_LENGTH = 300;
const KEY_PATTERN = /^[a-z0-9][a-z0-9-]{0,63}$/;

const DEFAULT_PROFILE = 'orchestrator';

export function toolLabel(tool: string): string {
  return toolInfo(tool).label;
}

export type KnowledgeScope = 'all' | 'folders';

export const HANDOVER_POLICIES: ReadonlyArray<{ value: HandoverPolicy; label: string; description: string; summary: string }> = [
  { value: 'never', label: 'Never', description: 'It does not offer a person. Visitors can still use the Talk to a person button.', summary: 'never offers a person' },
  { value: 'onRequest', label: 'When asked', description: 'It brings in a person when the visitor asks for one.', summary: 'brings in a person when asked' },
  { value: 'whenUnsure', label: 'When asked or unsure', description: 'It also offers a person when your sources do not answer the question.', summary: 'offers a person when unsure' },
];

export interface AgentFormValues {
  name: string;
  key: string;
  /** Until the key is typed, it follows the name. */
  keyEdited: boolean;
  description: string;
  instructions: string;
  language: string;
  profileName: string;
  tools: string[];
  scope: KnowledgeScope;
  folderIds: Guid[];
  allowGeneralKnowledge: boolean;
  handoverPolicy: HandoverPolicy;
  /** Left out of the request until the agent has some or one is turned on. */
  mcpApps?: AgentMcpAppDto[];
  /** Left out of the request until some are picked. */
  specialists?: AgentSpecialistDto[];
  /** Left out (and not sent) while off and untouched. */
  showSpecialistName?: boolean;
}

export type AgentFormField = 'name' | 'key' | 'description' | 'instructions' | 'language' | 'profileName' | 'tools' | 'folderIds' | 'specialists';
export type AgentFormErrors = Partial<Record<AgentFormField, string>>;

/** Fields with a control in the form; validation messages for other fields are listed together. */
export const AGENT_FORM_FIELDS: readonly AgentFormField[] = ['name', 'key', 'description', 'instructions', 'language', 'profileName', 'tools', 'folderIds', 'specialists'];

function defaultProfile(options: AgentOptionsDto): string {
  return options.profiles.includes(DEFAULT_PROFILE) ? DEFAULT_PROFILE : (options.profiles[0] ?? DEFAULT_PROFILE);
}

/** A new agent from a template; without one it may use every tool, search every source and bring in a person when asked. */
export function templateValues(template: AgentTemplate | undefined, options: AgentOptionsDto): AgentFormValues {
  return {
    name: template?.name ?? '',
    key: keyFromName(template?.name ?? ''),
    keyEdited: false,
    description: template?.description ?? '',
    instructions: template?.instructions ?? '',
    language: '',
    profileName: defaultProfile(options),
    tools: template?.tools ? options.tools.filter((tool) => template.tools!.includes(tool)) : [...options.tools],
    scope: 'all',
    folderIds: [],
    allowGeneralKnowledge: template?.allowGeneralKnowledge ?? false,
    handoverPolicy: template?.handoverPolicy ?? 'onRequest',
  };
}

export function copyValues(agent: AgentDto, options: AgentOptionsDto): AgentFormValues {
  const values = initialAgentValues(agent, options);
  const name = `${agent.name} copy`.slice(0, MAX_NAME_LENGTH);
  return { ...values, name, key: keyFromName(name), keyEdited: false };
}

export function initialAgentValues(agent: AgentDto | null, options: AgentOptionsDto): AgentFormValues {
  if (!agent) {
    return templateValues(undefined, options);
  }
  return {
    name: agent.name,
    key: agent.key,
    keyEdited: true,
    description: agent.description ?? '',
    instructions: agent.instructions ?? '',
    language: agent.language ?? '',
    profileName: agent.profileName || defaultProfile(options),
    tools: [...agent.tools],
    scope: agent.folderIds.length > 0 ? 'folders' : 'all',
    folderIds: [...agent.folderIds],
    allowGeneralKnowledge: agent.allowGeneralKnowledge,
    handoverPolicy: agent.handoverPolicy,
    ...(agent.specialists && agent.specialists.length > 0 ? { specialists: agent.specialists.map((specialist) => ({ ...specialist })) } : {}),
    ...(agent.showSpecialistName ? { showSpecialistName: true } : {}),
    ...(agent.mcpApps && agent.mcpApps.length > 0
      ? { mcpApps: agent.mcpApps.map((app) => ({ ...app, tools: app.tools.map((tool) => ({ ...tool })) })) }
      : {}),
  };
}

/** "Website helper" → "website-helper". */
export function keyFromName(name: string): string {
  return name
    .normalize('NFKD')
    .replace(/[̀-ͯ]/g, '')
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+/, '')
    .slice(0, MAX_KEY_LENGTH)
    .replace(/-+$/, '');
}

export interface SaveAgentResult {
  request: SaveAgentRequest | null;
  errors: AgentFormErrors;
}

export interface SaveAgentContext {
  isNew: boolean;
  options: AgentOptionsDto;
  /** Drops chosen folders deleted since; the API refuses unknown folders. Undefined while the folders load. */
  knownFolderIds?: ReadonlySet<Guid>;
}

/** The whole agent, since PUT replaces it; the key only on create. */
export function toSaveRequest(values: AgentFormValues, { isNew, options, knownFolderIds }: SaveAgentContext): SaveAgentResult {
  const errors: AgentFormErrors = {};
  const name = values.name.trim();
  if (!name) {
    errors.name = 'Enter a name.';
  }
  const key = values.key.trim();
  if (isNew && !KEY_PATTERN.test(key)) {
    errors.key = 'Use 1 to 64 lowercase letters, digits or dashes, starting with a letter or digit.';
  }
  const folderIds = values.scope === 'folders' ? values.folderIds.filter((id) => !knownFolderIds || knownFolderIds.has(id)) : [];
  // An empty list would mean every source.
  if (values.scope === 'folders' && folderIds.length === 0) {
    errors.folderIds = 'Choose at least one folder, or All sources.';
  }
  const specialists = values.specialists?.map((specialist) => ({ agentId: specialist.agentId, whenToUse: specialist.whenToUse.trim() }));
  if (specialists?.some((specialist) => !specialist.whenToUse)) {
    errors.specialists = 'Say when to hand off to each specialist.';
  }
  if (Object.keys(errors).length > 0) {
    return { request: null, errors };
  }
  return {
    request: {
      name,
      ...(isNew ? { key } : {}),
      description: values.description.trim() || null,
      instructions: values.instructions.trim() || null,
      language: values.language.trim() || null,
      profileName: values.profileName || null,
      folderIds,
      tools: options.tools.filter((tool) => values.tools.includes(tool)),
      allowGeneralKnowledge: values.allowGeneralKnowledge,
      handoverPolicy: values.handoverPolicy,
      ...(values.mcpApps ? { mcpApps: keptApps(values.mcpApps) } : {}),
      ...(specialists ? { specialists } : {}),
      ...(values.showSpecialistName !== undefined ? { showSpecialistName: values.showSpecialistName } : {}),
    },
    errors,
  };
}

/** An app turned on and off again without any choice is the same as one never touched, so it is not kept. */
function keptApps(apps: readonly AgentMcpAppDto[]): AgentMcpAppDto[] {
  return apps.filter((app) => app.enabled || app.mode === 'chosen' || app.tools.length > 0 || app.changesPolicy !== 'confirm');
}

/** A chosen folder covers its subfolders, so they leave the selection. */
export function toggleFolder(selected: readonly Guid[], node: FolderNode): Guid[] {
  if (selected.includes(node.folder.id)) {
    return selected.filter((id) => id !== node.folder.id);
  }
  const inside = subtreeIds(node);
  return [...selected.filter((id) => !inside.has(id)), node.folder.id];
}

export function coveringFolder(tree: readonly FolderNode[], selected: ReadonlySet<Guid>, id: Guid): FolderNode | undefined {
  return folderPath(tree, id)
    .slice(0, -1)
    .find((node) => selected.has(node.folder.id));
}

export function scopeSummary(folderIds: readonly Guid[], tree: readonly FolderNode[] | undefined): string {
  if (folderIds.length === 0) {
    return 'All sources';
  }
  if (!tree) {
    return pluralize(folderIds.length, 'folder');
  }
  const known = folderIds.filter((id) => findNode(tree, id)).map((id) => folderPathLabel(tree, id));
  const missing = folderIds.length - known.length;
  return [...known, ...(missing > 0 ? [pluralize(missing, 'deleted folder')] : [])].join(', ');
}

export type AgentSection = 'general' | 'behavior' | 'knowledge' | 'tools' | 'handover' | 'specialists' | 'advanced';

export const SECTION_FIELDS: Readonly<Record<AgentSection, ReadonlyArray<keyof AgentFormValues>>> = {
  general: ['name', 'description', 'language'],
  behavior: ['instructions'],
  knowledge: ['scope', 'folderIds', 'allowGeneralKnowledge'],
  tools: ['tools', 'mcpApps'],
  handover: ['handoverPolicy'],
  specialists: ['specialists', 'showSpecialistName'],
  advanced: ['profileName'],
};

/** Whether the fields hold the same values; the order of tools and folders does not matter. */
export function sameFields(a: AgentFormValues, b: AgentFormValues, fields: ReadonlyArray<keyof AgentFormValues>): boolean {
  const comparable = (values: AgentFormValues) =>
    JSON.stringify(
      fields.map((field) =>
        field === 'mcpApps'
          ? keptApps(values.mcpApps ?? [])
          : field === 'specialists'
            ? (values.specialists ?? []).map((specialist) => [specialist.agentId, specialist.whenToUse.trim()])
            : field === 'showSpecialistName'
              ? values.showSpecialistName === true
              : Array.isArray(values[field])
            ? [...(values[field] as string[])].sort()
            : values[field],
      ),
    );
  return comparable(a) === comparable(b);
}

/** The saved agent with one section's draft on top, since PUT replaces the whole agent. */
export function withSection(saved: AgentFormValues, draft: AgentFormValues, section: AgentSection): AgentFormValues {
  const next = { ...saved };
  for (const field of SECTION_FIELDS[section]) {
    (next as Record<string, unknown>)[field] = draft[field];
  }
  return next;
}
