import { EDIT_TARGETS, type EditTarget, type PreviewScene } from '../../../widget/shared/preview';
import { COLOR_TOKENS, type ColorToken } from '../../../widget/shared/theme';
import type { AgentDto } from '../../agents/types';
import type { IntegrationsDto } from '../../notifications/types';
import type { WidgetOptionsDto } from '../types';
import {
  MAX_GREETING_LENGTH,
  MAX_LAUNCHER_LABEL_LENGTH,
  MAX_NAME_LENGTH,
  MAX_PLACEHOLDER_LENGTH,
  MAX_QUESTION_LENGTH,
  MAX_QUESTIONS,
  MAX_SLACK_CHANNEL_LENGTH,
  MAX_SUBTITLE_LENGTH,
  MAX_TEXT_LENGTH,
  MAX_TITLE_LENGTH,
  NUMBER_LIMITS,
  splitLines,
  type ColorMode,
  type NumberField,
  type WidgetField,
  type WidgetFormValues,
} from '../widgetForm';
import {
  CITATION_MODE_LABELS,
  CONTACT_CAPTURE_LABELS,
  DEFAULT_TEXTS,
  HANDOVER_CHANNEL_LABELS,
  POSITION_LABELS,
  textHint,
  textLabel,
  COLOR_LABELS,
  MODE_LABELS,
} from '../widgetTexts';
import type { ColorPath, DraftPath, TextPath } from './draft';

// Every setting the editor can change, defined once; the side panel, tabs and search all render these.

type Values = WidgetFormValues;
type KeysWith<T> = { [K in keyof Values]-?: Values[K] extends T ? K : never }[keyof Values];

export interface Notice {
  tone: 'info' | 'warning';
  title?: string;
  text: string;
  link?: { to: string; label: string };
}

interface NoticeContext {
  integrations: IntegrationsDto | undefined;
  agents: readonly AgentDto[];
}

interface Common {
  /** The draft path the property changes; also its id. */
  id: DraftPath;
  label: string;
  field?: WidgetField;
  hint?: string;
  optional?: boolean;
  when?: (values: Values) => boolean;
  notice?: (values: Values, context: NoticeContext) => Notice | null;
}

export type PropertyDef =
  | (Common & { kind: 'text'; id: KeysWith<string> | TextPath; maxLength: number; placeholder?: string; mono?: boolean })
  | (Common & { kind: 'longText'; id: KeysWith<string>; rows: number; maxLength?: number; placeholder?: string; mono?: boolean })
  | (Common & { kind: 'color'; id: 'brandColor' })
  | (Common & { kind: 'icon'; id: 'launcherIcon'; choices: readonly string[] })
  | (Common & { kind: 'image'; id: 'avatarAssetId' | 'launcherAssetId'; round?: boolean })
  | (Common & { kind: 'toggle'; id: KeysWith<boolean> })
  | (Common & {
      kind: 'choice';
      id: 'theme' | 'position' | 'citationMode' | 'contactCapture' | 'channel';
      choices: readonly string[];
      labels: Readonly<Record<string, string>>;
      display: 'radio' | 'select';
    })
  | (Common & { kind: 'list'; id: 'suggestedQuestions'; maxItems: number; maxLength: number; itemLabel: string })
  | (Common & { kind: 'number'; id: NumberField })
  | (Common & { kind: 'agent'; id: 'agentId' })
  | (Common & { kind: 'preset'; id: 'preset'; choices: readonly string[] })
  | (Common & { kind: 'colorToken'; id: ColorPath; mode: ColorMode; token: ColorToken })
  | (Common & { kind: 'timeZone'; id: 'timeZone' })
  | (Common & { kind: 'hours'; id: 'hours' });

export type TargetArea = 'launcher' | 'header' | 'messages' | 'bottom' | 'states';

export interface TargetDef {
  id: EditTarget;
  label: string;
  area: TargetArea;
  scene: PreviewScene;
  /** Outlined while the element doesn't show, such as for an empty subtitle. */
  parent?: EditTarget;
  properties: readonly DraftPath[];
}

export type SectionId = 'theme' | 'general' | 'security' | 'handover' | 'conversations' | 'texts';

export interface PanelSection {
  id: SectionId;
  label: string;
  properties: readonly DraftPath[];
}

type Location = { kind: 'target'; target: EditTarget } | { kind: 'section'; section: SectionId };

export interface Registry {
  properties: ReadonlyMap<DraftPath, PropertyDef>;
  targets: readonly TargetDef[];
  sections: readonly PanelSection[];
  target: (id: EditTarget) => TargetDef;
  fallbacks: (id: EditTarget) => EditTarget[];
  /** The element whose main setting it is, else the first one that shows it, else a panel section. */
  locate: (id: DraftPath) => Location | null;
}

export function controlIdOf(id: (name: string) => string, property: DraftPath): string {
  return id(`property-${property}`);
}

export const AREA_LABELS: Readonly<Record<TargetArea, string>> = {
  launcher: 'Launcher',
  header: 'Header',
  messages: 'Messages',
  bottom: 'Below the messages',
  states: 'Handover and end',
};

export const SCENE_LABELS: Readonly<Record<PreviewScene, string>> = {
  launcher: 'Launcher',
  start: 'Start',
  answering: 'Answering',
  conversation: 'Conversation',
  contact: 'Contact form',
  waiting: 'Waiting for a person',
  joined: 'A person joined',
  ended: 'Chat ended',
};

function colors(...tokens: ColorToken[]): ColorPath[] {
  return tokens.flatMap((token): ColorPath[] => [`colors.light.${token}`, `colors.dark.${token}`]);
}

// Keyed by every EditTarget, so the compiler checks that each one has a definition.
const TARGETS: { readonly [T in EditTarget]: Omit<TargetDef, 'id'> } = {
  'appearance.launcher': {
    label: 'Launcher',
    area: 'launcher',
    scene: 'launcher',
    properties: ['launcherIcon', 'launcherAssetId', 'launcherLabel', 'position', 'texts.launcherLabel', ...colors('launcher', 'launcherIcon', 'glow')],
  },
  'appearance.header': { label: 'Header', area: 'header', scene: 'start', properties: colors('header', 'headerText') },
  'appearance.avatar': { label: 'Avatar', area: 'header', scene: 'start', parent: 'appearance.header', properties: ['avatarAssetId'] },
  'appearance.title': { label: 'Title', area: 'header', scene: 'start', parent: 'appearance.header', properties: ['title', 'subtitle'] },
  'appearance.subtitle': { label: 'Subtitle', area: 'header', scene: 'start', parent: 'appearance.title', properties: ['subtitle'] },
  'header.menu': {
    label: 'Menu',
    area: 'header',
    scene: 'conversation',
    parent: 'appearance.header',
    properties: ['texts.menu', 'texts.newChat', 'texts.endChat'],
  },
  'header.close': { label: 'Close button', area: 'header', scene: 'start', parent: 'appearance.header', properties: ['texts.close'] },
  'behavior.messages': {
    label: 'Messages',
    area: 'messages',
    scene: 'start',
    properties: ['greeting', 'suggestedQuestions', 'citationMode', 'feedbackEnabled', ...colors('panel', 'text', 'muted', 'border')],
  },
  'messages.visitor': {
    label: "Visitor's message",
    area: 'messages',
    scene: 'conversation',
    parent: 'behavior.messages',
    properties: colors('userBubble', 'userBubbleText'),
  },
  'messages.assistant': {
    label: "Assistant's answer",
    area: 'messages',
    scene: 'conversation',
    parent: 'behavior.messages',
    properties: colors('assistantBubble', 'assistantBubbleText'),
  },
  'behavior.greeting': {
    label: 'Greeting',
    area: 'messages',
    scene: 'start',
    parent: 'behavior.messages',
    properties: ['greeting', ...colors('assistantBubble', 'assistantBubbleText')],
  },
  'behavior.suggestedQuestions': {
    label: 'Suggested questions',
    area: 'messages',
    scene: 'start',
    parent: 'behavior.messages',
    properties: ['suggestedQuestions', ...colors('accent')],
  },
  'behavior.citations': {
    label: 'Sources',
    area: 'messages',
    scene: 'conversation',
    parent: 'behavior.messages',
    properties: ['citationMode', 'texts.sources', 'texts.source', 'texts.newTab'],
  },
  'behavior.feedback': {
    label: 'Feedback',
    area: 'messages',
    scene: 'conversation',
    parent: 'behavior.messages',
    properties: ['feedbackEnabled', 'texts.feedbackPrompt', 'texts.helpful', 'texts.notHelpful', 'texts.feedbackThanks', 'texts.feedbackComment', 'texts.skip'],
  },
  'behavior.handover': {
    label: 'Talk to a person',
    area: 'bottom',
    scene: 'conversation',
    parent: 'behavior.placeholder',
    properties: ['handoverEnabled', 'texts.talkToPerson', 'contactCapture'],
  },
  'behavior.placeholder': {
    label: 'Message box',
    area: 'bottom',
    scene: 'start',
    properties: ['placeholder', ...colors('accent', 'onAccent'), 'texts.send', 'texts.message', 'texts.charactersLeft'],
  },
  'behavior.contact': {
    label: 'Contact form',
    area: 'bottom',
    scene: 'contact',
    properties: ['contactCapture', 'texts.contactTitle', 'texts.contactName', 'texts.contactEmail', 'texts.contactSubmit', 'texts.cancel'],
  },
  'status.waiting': { label: 'Waiting for a person', area: 'states', scene: 'waiting', properties: ['texts.waitingForPerson', 'texts.keepChatting'] },
  'status.joined': { label: 'A person joined', area: 'states', scene: 'joined', properties: ['texts.personJoined', 'texts.team'] },
  'status.ended': { label: 'Chat ended', area: 'states', scene: 'ended', properties: ['texts.ended', 'texts.ratingPrompt', 'texts.rating', 'texts.newChat'] },
  'texts.poweredBy': { label: 'Footer', area: 'bottom', scene: 'start', parent: 'behavior.placeholder', properties: ['showFooter', 'texts.poweredBy'] },
};

const SETTING_SECTIONS: readonly PanelSection[] = [
  { id: 'theme', label: 'Theme', properties: ['preset', 'brandColor', 'theme', ...colors(...COLOR_TOKENS)] },
  { id: 'general', label: 'General', properties: ['name', 'agentId', 'enabled'] },
  {
    id: 'security',
    label: 'Security and limits',
    properties: ['allowedOrigins', 'messagesPerMinute', 'dailyMessageCap', 'maxMessageLength', 'retentionDays', 'requireVerifiedUsers'],
  },
  {
    id: 'handover',
    label: 'Handover',
    properties: ['handoverEnabled', 'channel', 'emailTo', 'slackChannelId', 'escalateAfterMinutes', 'waitMinutes', 'alwaysAvailable', 'timeZone', 'hours'],
  },
  { id: 'conversations', label: 'Conversations', properties: ['closeIdle', 'aiIdleMinutes', 'humanIdleHours'] },
];

function numberProperty(id: NumberField, label: string, hint: string, when?: (values: Values) => boolean): PropertyDef {
  return { id, kind: 'number', label, field: NUMBER_LIMITS[id].path, hint, ...(when ? { when } : {}) };
}

function channelNotice(values: Values, { integrations }: NoticeContext): Notice | null {
  if (!integrations) {
    return null;
  }
  const setUp = { to: '/notifications', label: 'Open Notifications' };
  if (values.channel === 'email' && !integrations.email.canSend) {
    return { tone: 'warning', text: 'Email is not set up, so requests stay in the inbox.', link: setUp };
  }
  if (values.channel === 'email' && splitLines(values.emailTo).length === 0 && !integrations.email.supportAddress) {
    return { tone: 'warning', text: 'No address to send to. Add one below or a support address in Connections.', link: setUp };
  }
  if (values.channel === 'slack' && !integrations.slack.canPost) {
    return { tone: 'warning', text: 'Slack is not set up, so requests go by email or stay in the inbox.', link: setUp };
  }
  if (values.channel === 'slack' && !values.slackChannelId.trim() && !integrations.slack.channelId) {
    return { tone: 'warning', text: 'No Slack channel. Add one below or in Connections.', link: setUp };
  }
  return null;
}

function agentHandoverNotice(values: Values, { agents }: NoticeContext): Notice {
  const agent = agents.find((candidate) => candidate.id.toLowerCase() === values.agentId.toLowerCase());
  const text = 'When the assistant offers a person is set on the agent';
  return agent ? { tone: 'info', text, link: { to: `/agents/${agent.id}`, label: agent.name } } : { tone: 'info', text: `${text}.` };
}

const viaSlack = (values: Values) => values.channel === 'slack';
const onlyAtHours = (values: Values) => !values.alwaysAvailable;
const closesIdle = (values: Values) => values.closeIdle;

function settingProperties(options: WidgetOptionsDto): PropertyDef[] {
  return [
    { id: 'name', kind: 'text', label: 'Name', field: 'name', maxLength: MAX_NAME_LENGTH, placeholder: 'e.g. Shop help', hint: 'Only staff see it.' },
    { id: 'agentId', kind: 'agent', label: 'Agent', field: 'agentId' },
    { id: 'enabled', kind: 'toggle', label: 'Enabled', hint: 'A disabled widget does not show on any site.' },
    { id: 'title', kind: 'text', label: 'Title', field: 'settings.appearance.title', maxLength: MAX_TITLE_LENGTH },
    {
      id: 'subtitle',
      kind: 'text',
      label: 'Subtitle',
      field: 'settings.appearance.subtitle',
      maxLength: MAX_SUBTITLE_LENGTH,
      optional: true,
      placeholder: 'e.g. We usually answer in a minute',
    },
    {
      id: 'avatarAssetId',
      kind: 'image',
      label: 'Avatar',
      field: 'settings.appearance',
      round: true,
      hint: 'Next to the answers. PNG, JPEG or WebP, up to 512 KB.',
    },
    {
      id: 'launcherIcon',
      kind: 'icon',
      label: 'Icon',
      field: 'settings.appearance.launcherIcon',
      choices: options.launcherIcons,
      notice: (values) => (values.launcherAssetId ? { tone: 'info', text: 'The image below replaces the icon.' } : null),
    },
    { id: 'launcherAssetId', kind: 'image', label: 'Image', field: 'settings.appearance', hint: 'Replaces the icon. PNG, JPEG or WebP, up to 512 KB.' },
    {
      id: 'launcherLabel',
      kind: 'text',
      label: 'Label',
      field: 'settings.appearance.launcherLabel',
      maxLength: MAX_LAUNCHER_LABEL_LENGTH,
      optional: true,
      hint: 'Text next to the icon, such as Chat with us.',
    },
    {
      id: 'position',
      kind: 'choice',
      label: 'Position',
      field: 'settings.appearance.position',
      choices: options.positions,
      labels: POSITION_LABELS,
      display: 'radio',
    },
    { id: 'brandColor', kind: 'color', label: 'Brand color', field: 'settings.appearance.brandColor', hint: "For the launcher, the header and the visitor's messages." },
    { id: 'preset', kind: 'preset', label: 'Theme', field: 'settings.appearance.preset', choices: options.presets },
    {
      id: 'theme',
      kind: 'choice',
      label: 'Mode',
      field: 'settings.appearance.theme',
      choices: options.themes,
      labels: MODE_LABELS,
      display: 'radio',
      hint: "The preview's light and dark switch stands for the visitor's system.",
    },
    { id: 'showFooter', kind: 'toggle', label: 'Show the footer' },
    {
      id: 'greeting',
      kind: 'longText',
      label: 'Greeting',
      field: 'settings.behavior.greeting',
      rows: 3,
      maxLength: MAX_GREETING_LENGTH,
      optional: true,
      hint: 'The first message of the chat.',
    },
    {
      id: 'suggestedQuestions',
      kind: 'list',
      label: 'Suggested questions',
      field: 'settings.behavior.suggestedQuestions',
      maxItems: MAX_QUESTIONS,
      maxLength: MAX_QUESTION_LENGTH,
      itemLabel: 'Question',
      hint: `Shown before the first message. Up to ${MAX_QUESTIONS}, each up to ${MAX_QUESTION_LENGTH} characters.`,
    },
    {
      id: 'placeholder',
      kind: 'text',
      label: 'Placeholder',
      field: 'settings.behavior.placeholder',
      maxLength: MAX_PLACEHOLDER_LENGTH,
      optional: true,
      placeholder: DEFAULT_TEXTS.placeholder ?? '',
    },
    {
      id: 'citationMode',
      kind: 'choice',
      label: 'Sources of answers',
      field: 'settings.behavior.citationMode',
      choices: options.citationModes,
      labels: CITATION_MODE_LABELS,
      display: 'radio',
    },
    { id: 'feedbackEnabled', kind: 'toggle', label: 'Visitors can rate answers' },
    { id: 'handoverEnabled', kind: 'toggle', label: 'Show Talk to a person', notice: agentHandoverNotice },
    {
      id: 'contactCapture',
      kind: 'choice',
      label: 'Ask for name and email',
      field: 'settings.behavior.contactCapture',
      choices: options.contactCaptures,
      labels: CONTACT_CAPTURE_LABELS,
      display: 'select',
      hint: 'With an email address, the team can reply after the visitor leaves.',
    },
    {
      id: 'allowedOrigins',
      kind: 'longText',
      label: 'Allowed sites',
      field: 'settings.security.allowedOrigins',
      rows: 3,
      mono: true,
      placeholder: 'https://www.example.com',
      hint: 'One per line, such as https://www.example.com or https://*.example.com for every subdomain.',
      notice: (values) =>
        splitLines(values.allowedOrigins).length === 0
          ? { tone: 'warning', title: 'Any site can embed this widget', text: 'Add your sites so the chat only runs there.' }
          : null,
    },
    numberProperty('messagesPerMinute', 'Messages per minute', 'Per visitor, from 1 to 60.'),
    numberProperty('dailyMessageCap', 'Messages per day', 'For the whole widget, from 1 to 100,000.'),
    numberProperty('maxMessageLength', 'Longest message', 'Characters, from 100 to 4,000.'),
    numberProperty('retentionDays', 'Keep conversations', 'Days, from 1 to 3,650.'),
    {
      id: 'channel',
      kind: 'choice',
      label: 'Send requests to',
      field: 'settings.handover.channel',
      choices: options.handoverChannels,
      labels: HANDOVER_CHANNEL_LABELS,
      display: 'select',
      hint: 'Requests come from the Talk to a person button or from the assistant. The inbox always gets them.',
      notice: channelNotice,
    },
    {
      id: 'emailTo',
      kind: 'longText',
      label: 'Email addresses',
      field: 'settings.handover.emailTo',
      rows: 2,
      mono: true,
      optional: true,
      hint: 'One per line. Empty uses the support address.',
      when: (values) => values.channel === 'email',
    },
    {
      id: 'slackChannelId',
      kind: 'text',
      label: 'Slack channel id',
      field: 'settings.handover.slackChannelId',
      maxLength: MAX_SLACK_CHANNEL_LENGTH,
      mono: true,
      optional: true,
      placeholder: 'C0123456789',
      hint: 'Empty uses the channel set in Connections.',
      when: viaSlack,
    },
    numberProperty(
      'escalateAfterMinutes',
      'Email after minutes',
      'Without a reply on Slack by then, the request also goes by email when the visitor left an address.',
      viaSlack,
    ),
    numberProperty(
      'waitMinutes',
      'Stop waiting for a person after (minutes)',
      'Then the visitor goes on with the assistant, and the team can still reply. From 1 to 1,440.',
    ),
    { id: 'alwaysAvailable', kind: 'toggle', label: 'Someone answers at any time' },
    {
      id: 'requireVerifiedUsers',
      kind: 'toggle',
      label: 'Only signed-in users',
      field: 'settings.security.requireVerifiedUsers',
      hint: 'The chat shows only to users your website identifies. Turn on identity verification under Embed code first.',
    },
    { id: 'timeZone', kind: 'timeZone', label: 'Time zone', field: 'settings.handover.availability.timeZone', hint: 'Such as Europe/Prague or UTC.', when: onlyAtHours },
    { id: 'hours', kind: 'hours', label: 'Opening hours', field: 'settings.handover.availability.hours', when: onlyAtHours },
    { id: 'closeIdle', kind: 'toggle', label: 'Close quiet conversations', hint: "The visitor's next message starts a new chat." },
    numberProperty('aiIdleMinutes', 'AI chats after (minutes)', 'After the last message, from 5 to 1,440.', closesIdle),
    numberProperty(
      'humanIdleHours',
      'Chats with a person after (hours)',
      "After the team's last reply, from 1 to 168. Never while the visitor waits for an answer.",
      closesIdle,
    ),
  ];
}

function textProperty(key: string): PropertyDef {
  const hint = textHint(key);
  return {
    id: `texts.${key}`,
    kind: 'text',
    label: textLabel(key),
    maxLength: MAX_TEXT_LENGTH,
    placeholder: DEFAULT_TEXTS[key] ?? '',
    ...(hint ? { hint } : {}),
  };
}

// The API reports all colors of a mode together, so their errors also show in the editor's problem list.
function colorProperty(mode: ColorMode, token: ColorToken): PropertyDef {
  return { id: `colors.${mode}.${token}`, kind: 'colorToken', label: COLOR_LABELS[token] ?? token, mode, token };
}

function isColorToken(token: string): token is ColorToken {
  return (COLOR_TOKENS as readonly string[]).includes(token);
}

export function buildRegistry(options: WidgetOptionsDto): Registry {
  const properties = new Map<DraftPath, PropertyDef>();
  const tokens = options.colorTokens.filter(isColorToken);
  const colorProperties = (['light', 'dark'] as const).flatMap((mode) => tokens.map((token) => colorProperty(mode, token)));
  for (const property of [...settingProperties(options), ...colorProperties, ...options.textKeys.map(textProperty)]) {
    properties.set(property.id, property);
  }
  // Leave out texts and colors this API doesn't know; it would reject them.
  const known = (path: DraftPath) => properties.has(path);
  const targets: TargetDef[] = EDIT_TARGETS.map((id) => ({ id, ...TARGETS[id], properties: TARGETS[id].properties.filter(known) }));
  const onCanvas = new Set(targets.flatMap((target) => target.properties));
  const sections: PanelSection[] = [
    ...SETTING_SECTIONS.map((section) => ({ ...section, properties: section.properties.filter(known) })),
    { id: 'texts', label: 'Other texts', properties: options.textKeys.map((key): DraftPath => `texts.${key}`).filter((id) => !onCanvas.has(id)) },
  ];
  const byId = Object.fromEntries(targets.map((target) => [target.id, target])) as Record<EditTarget, TargetDef>;
  return {
    properties,
    targets,
    sections,
    target: (id) => byId[id],
    fallbacks: (id) => {
      const parents: EditTarget[] = [];
      for (let parent = byId[id].parent; parent; parent = byId[parent].parent) {
        parents.push(parent);
      }
      return parents;
    },
    locate: (id) => {
      const holding = targets.filter((candidate) => candidate.properties.includes(id));
      const main = holding.filter((candidate) => candidate.properties[0] === id).sort((a, b) => a.properties.length - b.properties.length)[0];
      const target = main ?? holding[0];
      if (target) {
        return { kind: 'target', target: target.id };
      }
      const section = sections.find((candidate) => candidate.properties.includes(id));
      return section ? { kind: 'section', section: section.id } : null;
    },
  };
}
