import { Bot, CircleHelp, Headset, MessageCircle, MessageSquare, MessagesSquare, Sparkles, type LucideIcon } from 'lucide-react';
import { DEFAULT_TEXTS as WIDGET_DEFAULT_TEXTS } from '../../widget/app/texts';
import type { Weekday } from './types';

export const LAUNCHER_ICONS: Readonly<Record<string, { icon: LucideIcon; label: string }>> = {
  'message-circle': { icon: MessageCircle, label: 'Speech bubble' },
  'message-square': { icon: MessageSquare, label: 'Square bubble' },
  'messages-square': { icon: MessagesSquare, label: 'Two bubbles' },
  sparkles: { icon: Sparkles, label: 'Sparkles' },
  bot: { icon: Bot, label: 'Robot' },
  'circle-help': { icon: CircleHelp, label: 'Question mark' },
  headset: { icon: Headset, label: 'Headset' },
};

/** The stored `theme` setting is the mode; the UI's "theme" is the preset. */
export const MODE_LABELS: Readonly<Record<string, string>> = {
  auto: "Follow the visitor's system",
  light: 'Light',
  dark: 'Dark',
};

export const PRESET_LABELS: Readonly<Record<string, { label: string; description: string }>> = {
  glow: { label: 'Glow', description: 'Glass with a soft glow' },
  glass: { label: 'Glass', description: 'Clear glass, calm' },
  solid: { label: 'Solid', description: 'Opaque, no effects' },
};

export const COLOR_LABELS: Readonly<Record<string, string>> = {
  accent: 'Accent',
  onAccent: 'Text on accent',
  panel: 'Panel tint',
  text: 'Text',
  muted: 'Muted text',
  border: 'Border',
  header: 'Header',
  headerText: 'Header text',
  userBubble: "Visitor's bubble",
  userBubbleText: "Visitor's bubble text",
  assistantBubble: "Assistant's bubble",
  assistantBubbleText: "Assistant's bubble text",
  launcher: 'Launcher',
  launcherIcon: 'Launcher icon',
  glow: 'Glow',
};

export const POSITION_LABELS: Readonly<Record<string, string>> = {
  right: 'Bottom right',
  left: 'Bottom left',
};

export const CITATION_MODE_LABELS: Readonly<Record<string, string>> = {
  hidden: 'Hide sources',
  titles: 'Show source titles',
  links: 'Link sources that are web pages',
};

export const CONTACT_CAPTURE_LABELS: Readonly<Record<string, string>> = {
  never: 'Never',
  before_handover: 'Before a person joins',
  before_chat: 'Before the chat starts',
};

export const HANDOVER_CHANNEL_LABELS: Readonly<Record<string, string>> = {
  portal: 'Inbox only',
  email: 'Email',
  slack: 'Slack',
};

export const WEEKDAY_LABELS: Readonly<Record<Weekday, string>> = {
  monday: 'Monday',
  tuesday: 'Tuesday',
  wednesday: 'Wednesday',
  thursday: 'Thursday',
  friday: 'Friday',
  saturday: 'Saturday',
  sunday: 'Sunday',
};

export const DEFAULT_TEXTS: Readonly<Record<string, string>> = WIDGET_DEFAULT_TEXTS;

const TEXT_LABELS: Readonly<Record<string, string>> = {
  launcherLabel: 'Launcher name for screen readers',
  close: 'Close button',
  send: 'Send button',
  newChat: 'New chat button',
  endChat: 'End chat button',
  talkToPerson: 'Talk to a person button',
  waitingForPerson: 'Waiting for a person',
  keepChatting: 'Keep chatting button',
  personJoined: 'A person joined',
  feedbackPrompt: 'Feedback question',
  feedbackThanks: 'Feedback thanks',
  feedbackComment: 'Feedback comment',
  ratingPrompt: 'Chat rating question',
  contactTitle: 'Contact form title',
  contactName: 'Contact name field',
  contactEmail: 'Contact email field',
  contactSubmit: 'Contact form button',
  sources: 'Sources heading',
  offline: 'Offline notice',
  rateLimited: 'Too many messages',
  dailyCap: 'Daily limit reached',
  error: 'Error message',
  poweredBy: 'Footer',
  message: 'Message box name for screen readers',
  messages: 'Message list name for screen readers',
  placeholder: 'Default placeholder',
  menu: 'Menu button',
  thinking: 'Writing an answer',
  searching: 'Searching',
  helpful: 'Helpful button',
  notHelpful: 'Not helpful button',
  skip: 'Skip button',
  cancel: 'Cancel button',
  dismiss: 'Dismiss button',
  ended: 'Chat ended',
  busy: 'Answer still being written',
  charactersLeft: 'Characters left',
  you: 'You, for screen readers',
  team: 'Team, when a person has no name',
  rating: 'Star rating for screen readers',
  unavailable: 'Chat not available',
  retry: 'Try again button',
  loading: 'Loading',
  newTab: 'Opens in a new tab, for screen readers',
  source: 'Source number for screen readers',
};

const TEXT_HINTS: Readonly<Record<string, string>> = {
  personJoined: '{name} becomes the name of the person.',
  charactersLeft: '{count} becomes the number of characters.',
  rating: '{count} becomes the number of stars.',
  source: '{count} becomes the number of the source.',
};

export function textHint(key: string): string | undefined {
  return TEXT_HINTS[key];
}

/** "talkToPerson" → "Talk to person" for keys this portal does not know. */
export function textLabel(key: string): string {
  const known = TEXT_LABELS[key];
  if (known) {
    return known;
  }
  const spaced = key.replace(/([a-z0-9])([A-Z])/g, '$1 $2').toLowerCase();
  return spaced.charAt(0).toUpperCase() + spaced.slice(1);
}

export function choiceLabel(labels: Readonly<Record<string, string>>, value: string): string {
  return labels[value] ?? value;
}
