import { describe, expect, it } from 'vitest';
import { EDIT_TARGETS, PREVIEW_SCENES } from '../../../widget/shared/preview';
import { COLOR_TOKENS } from '../../../widget/shared/theme';
import { agentDto } from '../../../test/agentsApi';
import { emailSettings, slackSettings, workingEmail } from '../../../test/integrationsApi';
import { WIDGET_TEXT_KEYS, widgetDto, widgetOptions } from '../../../test/widgetsApi';
import { initialWidgetValues, WIDGET_FIELDS, type WidgetFormValues } from '../widgetForm';
import type { DraftPath } from './draft';
import { buildRegistry, controlIdOf } from './registry';

const options = widgetOptions();
const registry = buildRegistry(options);
const values = initialWidgetValues(widgetDto(), options.defaults);
const FORM_KEYS = Object.keys(values).filter((key) => key !== 'texts' && key !== 'colors') as Array<keyof WidgetFormValues>;

describe('widget property registry', () => {
  it('defines every setting of the form once, one text property for each text key and one color per token and mode', () => {
    const ids = [...registry.properties.keys()];
    expect(new Set(ids).size).toBe(ids.length);
    expect(ids.filter((id) => !id.startsWith('texts.') && !id.startsWith('colors.')).sort()).toEqual([...FORM_KEYS].sort());
    expect(ids.filter((id) => id.startsWith('texts.'))).toEqual(WIDGET_TEXT_KEYS.map((key) => `texts.${key}`));
    expect(ids.filter((id) => id.startsWith('colors.'))).toEqual(['light', 'dark'].flatMap((mode) => COLOR_TOKENS.map((token) => `colors.${mode}.${token}`)));
    expect(registry.properties.get('colors.dark.header')).toMatchObject({ kind: 'colorToken', label: 'Header', mode: 'dark', token: 'header' });
    expect(registry.properties.get('texts.talkToPerson')).toMatchObject({ kind: 'text', label: 'Talk to a person button', placeholder: 'Talk to a person', maxLength: 200 });
  });

  it('places every property: on an element of the preview, or in a side panel section', () => {
    for (const id of registry.properties.keys()) {
      expect(registry.locate(id), id).not.toBeNull();
    }
    expect(registry.locate('title')).toEqual({ kind: 'target', target: 'appearance.title' });
    expect(registry.locate('subtitle')).toEqual({ kind: 'target', target: 'appearance.subtitle' });
    expect(registry.locate('brandColor')).toEqual({ kind: 'section', section: 'theme' });
    expect(registry.targets.some((target) => target.properties.includes('brandColor'))).toBe(false);
    expect(registry.locate('greeting')).toEqual({ kind: 'target', target: 'behavior.greeting' });
    expect(registry.locate('feedbackEnabled')).toEqual({ kind: 'target', target: 'behavior.feedback' });
    expect(registry.locate('texts.newChat')).toEqual({ kind: 'target', target: 'header.menu' });
    expect(registry.locate('allowedOrigins')).toEqual({ kind: 'section', section: 'security' });
    expect(registry.locate('texts.rateLimited')).toEqual({ kind: 'section', section: 'texts' });
    const panelTexts = registry.sections.find((section) => section.id === 'texts')!.properties;
    expect(panelTexts).not.toContain('texts.talkToPerson');
    expect(panelTexts).toEqual(expect.arrayContaining(['texts.offline', 'texts.rateLimited', 'texts.dailyCap', 'texts.error']));
  });

  it('describes every element the preview marks, in a scene that shows it, with parents that are elements too', () => {
    expect(registry.targets.map((target) => target.id)).toEqual([...EDIT_TARGETS]);
    for (const target of registry.targets) {
      expect(PREVIEW_SCENES, target.id).toContain(target.scene);
      expect(target.properties.length, target.id).toBeGreaterThan(0);
      for (const id of target.properties) {
        expect(registry.properties.has(id), `${target.id}: ${id}`).toBe(true);
      }
      if (target.parent) {
        expect(registry.target(target.parent).id).toBe(target.parent);
      }
    }
    expect(registry.target('appearance.subtitle').parent).toBe('appearance.title');
  });

  it('shows every validation error of the API somewhere', () => {
    const fields = new Set([...registry.properties.values()].map((property) => property.field));
    const groups = ['settings.behavior.texts', 'settings.appearance.colors.light', 'settings.appearance.colors.dark'];
    expect(WIDGET_FIELDS.filter((field) => !groups.includes(field) && !fields.has(field))).toEqual([]);
  });

  it('puts every color on an element that shows it and in the theme section, and the footer toggle on the footer', () => {
    const theme = registry.sections.find((section) => section.id === 'theme')!.properties;
    expect(theme.slice(0, 3)).toEqual(['preset', 'brandColor', 'theme']);
    for (const token of COLOR_TOKENS) {
      expect(theme).toContain(`colors.light.${token}`);
      expect(registry.targets.some((target) => target.properties.includes(`colors.dark.${token}`)), token).toBe(true);
    }
    expect(registry.target('messages.visitor').properties).toEqual(['colors.light.userBubble', 'colors.dark.userBubble', 'colors.light.userBubbleText', 'colors.dark.userBubbleText']);
    expect(registry.target('texts.poweredBy').properties).toEqual(['showFooter', 'texts.poweredBy']);
    expect(registry.locate('preset')).toEqual({ kind: 'section', section: 'theme' });
    expect(registry.properties.get('theme')).toMatchObject({ label: 'Mode' });
  });

  it('leaves out texts and colors the API does not know', () => {
    const older = buildRegistry(widgetOptions({ textKeys: ['send', 'close'], colorTokens: ['accent'] }));
    expect(older.target('behavior.placeholder').properties).toEqual(['placeholder', 'colors.light.accent', 'colors.dark.accent', 'texts.send']);
    expect(older.sections.find((section) => section.id === 'texts')!.properties).toEqual([]);
  });

  it('shows settings only while they apply, with warnings about what is not set up', () => {
    const property = (id: DraftPath) => registry.properties.get(id)!;
    expect(property('slackChannelId').when?.({ ...values, channel: 'slack' })).toBe(true);
    expect(property('slackChannelId').when?.({ ...values, channel: 'email' })).toBe(false);
    expect(property('hours').when?.({ ...values, alwaysAvailable: true })).toBe(false);

    const channel = property('channel').notice!;
    expect(channel({ ...values, channel: 'email' }, { integrations: { email: emailSettings(), slack: slackSettings() }, agents: [] })).toMatchObject({
      tone: 'warning',
      text: 'Email is not set up, so requests stay in the inbox.',
      link: { to: '/notifications' },
    });
    expect(channel({ ...values, channel: 'email', emailTo: 'help@shop.example' }, { integrations: { email: workingEmail, slack: slackSettings() }, agents: [] })).toBeNull();
    expect(property('allowedOrigins').notice!({ ...values, allowedOrigins: '' }, { integrations: undefined, agents: [] })?.title).toBe('Any site can embed this widget');
    expect(property('allowedOrigins').notice!(values, { integrations: undefined, agents: [] })).toBeNull();
  });

  it('points to the agent for when the assistant offers a person, beside the widget setting that offers one', () => {
    const handoverEnabled = registry.properties.get('handoverEnabled')!;
    expect(registry.sections.find((section) => section.id === 'handover')!.properties[0]).toBe('handoverEnabled');
    expect(registry.properties.has('offerWhenUnsure' as DraftPath)).toBe(false);
    const agent = agentDto();
    expect(handoverEnabled.notice!({ ...values, agentId: agent.id }, { integrations: undefined, agents: [agent] })).toEqual({
      tone: 'info',
      text: 'When the assistant offers a person is set on the agent',
      link: { to: `/agents/${agent.id}`, label: 'Website helper' },
    });
    expect(handoverEnabled.notice!({ ...values, agentId: '' }, { integrations: undefined, agents: [agent] })?.link).toBeUndefined();
  });

  it('keeps quiet conversations in a section of their own, the wait with the handover and the cancel text on the waiting element', () => {
    const property = (id: DraftPath) => registry.properties.get(id)!;
    expect(registry.sections.find((section) => section.id === 'conversations')).toEqual({
      id: 'conversations',
      label: 'Conversations',
      properties: ['closeIdle', 'aiIdleMinutes', 'humanIdleHours'],
    });
    expect(property('closeIdle')).toMatchObject({ kind: 'toggle', label: 'Close quiet conversations' });
    expect(property('aiIdleMinutes')).toMatchObject({
      kind: 'number',
      label: 'AI chats after (minutes)',
      field: 'settings.conversations.aiIdleMinutes',
    });
    expect(property('humanIdleHours')).toMatchObject({
      kind: 'number',
      label: 'Chats with a person after (hours)',
      field: 'settings.conversations.humanIdleHours',
    });
    expect(property('aiIdleMinutes').when?.({ ...values, closeIdle: false })).toBe(false);
    expect(property('humanIdleHours').when?.({ ...values, closeIdle: true })).toBe(true);

    expect(registry.locate('waitMinutes')).toEqual({ kind: 'section', section: 'handover' });
    expect(property('waitMinutes')).toMatchObject({ kind: 'number', label: 'Stop waiting for a person after (minutes)', field: 'settings.handover.waitMinutes' });
    expect(property('waitMinutes').when).toBeUndefined();

    expect(registry.target('status.waiting').properties).toEqual(['texts.waitingForPerson', 'texts.keepChatting']);
    expect(property('texts.keepChatting')).toMatchObject({ label: 'Keep chatting button', placeholder: 'Keep chatting with the assistant' });
    expect(registry.sections.find((section) => section.id === 'texts')!.properties).not.toContain('texts.keepChatting');
  });

  it('gives each property a control id of its own per editor', () => {
    expect(controlIdOf((name) => `e1-${name}`, 'texts.send')).toBe('e1-property-texts.send');
  });
});
