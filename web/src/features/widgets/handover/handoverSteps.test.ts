import { describe, expect, it } from 'vitest';
import { agentDto } from '../../../test/agentsApi';
import { emailSettings, slackSettings, workingEmail, workingSlack } from '../../../test/integrationsApi';
import { widgetDto, widgetSettings } from '../../../test/widgetsApi';
import type { AlertDto } from '../../alerts/types';
import { initialWidgetValues } from '../widgetForm';
import { buildHandoverFlow, handoverDraftFromForm, type HandoverContext, type HandoverDraft } from './handoverSteps';
import { layoutRows } from '../../../components/flow/rowsLayout';

const draft = (overrides: Partial<HandoverDraft> = {}): HandoverDraft => ({
  handoverEnabled: true,
  agentId: agentDto().id,
  channel: 'portal',
  emailTo: [],
  slackChannelId: '',
  escalateAfterMinutes: 10,
  waitMinutes: 15,
  alwaysAvailable: true,
  timeZone: 'Europe/Prague',
  openDays: 5,
  contactCapture: 'before_handover',
  ...overrides,
});

const context = (overrides: Partial<HandoverContext> = {}): HandoverContext => ({
  agents: [agentDto()],
  integrations: { email: workingEmail, slack: workingSlack },
  alerts: [],
  ...overrides,
});

const rowIds = (flow: ReturnType<typeof buildHandoverFlow>) => flow.rows.map((row) => row.map((step) => step.id));
const step = (flow: ReturnType<typeof buildHandoverFlow>, id: string) => flow.rows.flat().find((s) => s.id === id);

describe('buildHandoverFlow', () => {
  it('goes from the visitor or the agent to the Inbox, waits, then a person joins or the assistant takes over', () => {
    const flow = buildHandoverFlow(draft(), context());
    expect(rowIds(flow)).toEqual([['visitor', 'agent'], ['inbox'], ['wait'], ['joined', 'expired']]);
    expect(step(flow, 'agent')).toMatchObject({ title: 'Website helper', status: 'When asked', href: `/agents/${agentDto().id}` });
    expect(step(flow, 'visitor')?.lines).toEqual(['Leaves a name and email first']);
    expect(step(flow, 'wait')).toMatchObject({ title: 'Waits 15 minutes', lines: ['The visitor can keep chatting'] });
    expect(step(flow, 'expired')?.lines).toEqual(['Back to the assistant', 'Transcript goes by email']);
    expect(flow.edges.map((e) => `${e.source}>${e.target}`)).toEqual(['visitor>inbox', 'agent>inbox', 'inbox>wait', 'wait>joined', 'wait>expired']);
  });

  it('adds Slack with its health, emails after the escalation time, and sends away hours by email', () => {
    const flow = buildHandoverFlow(draft({ channel: 'slack', alwaysAvailable: false, openDays: 5 }), context());
    expect(rowIds(flow)).toEqual([['visitor', 'agent'], ['hours'], ['inbox', 'slack', 'email'], ['wait'], ['joined', 'expired']]);
    expect(step(flow, 'hours')).toMatchObject({ status: '5 days', lines: ['Europe/Prague'] });
    expect(step(flow, 'slack')).toMatchObject({ status: 'Connected', tone: 'success', lines: ['Away hours go by email'] });
    expect(step(flow, 'email')).toMatchObject({ status: 'Fallback', lines: ['When the team is away'] });
    expect(step(flow, 'wait')?.lines).toEqual(['Email after 10 minutes without a reply']);
    expect(flow.edges.find((e) => e.target === 'slack')?.label).toBe('open');
    expect(flow.edges.find((e) => e.target === 'email')?.label).toBe('away');
  });

  it('warns when the chosen channel cannot carry requests, and follows the fallback', () => {
    const slackOff = buildHandoverFlow(draft({ channel: 'slack' }), context({ integrations: { email: workingEmail, slack: slackSettings() } }));
    expect(step(slackOff, 'slack')).toMatchObject({ status: 'Not set up', tone: 'warning', problem: 'Not set up, so requests go by email' });
    expect(step(slackOff, 'email')).toMatchObject({ lines: ['Instead of Slack'] });

    const emailOff = buildHandoverFlow(draft({ channel: 'email' }), context({ integrations: { email: emailSettings(), slack: workingSlack } }));
    expect(step(emailOff, 'email')).toMatchObject({ tone: 'warning', problem: 'Not set up, so requests stay in the Inbox' });
    expect(step(emailOff, 'expired')?.lines).toEqual(['Back to the assistant']);

    const failing: AlertDto = { key: 'e', kind: 'email_failing', severity: 'critical', title: '', detail: '', count: 1, since: '2026-11-05T08:00:00Z', link: '/integrations' };
    const emailFailing = buildHandoverFlow(draft({ channel: 'email', emailTo: ['help@shop.example'] }), context({ alerts: [failing] }));
    expect(step(emailFailing, 'email')).toMatchObject({ status: 'Failing', tone: 'danger', lines: ['To help@shop.example'] });
  });

  it('reads the editor form values, and lays the rows out without overlaps', () => {
    const values = initialWidgetValues(widgetDto({ settings: widgetSettings({ behavior: { handoverEnabled: true }, handover: { channel: 'slack', waitMinutes: 20 } }) }), widgetSettings());
    const converted = handoverDraftFromForm(values);
    expect(converted).toMatchObject({ handoverEnabled: true, channel: 'slack', waitMinutes: 20, alwaysAvailable: true });

    const placed = layoutRows(buildHandoverFlow(converted, context()).rows, { width: 168 });
    for (const a of placed) {
      for (const b of placed) {
        if (a !== b) {
          expect(a.x + a.width <= b.x || b.x + b.width <= a.x || a.y + a.height <= b.y || b.y + b.height <= a.y).toBe(true);
        }
      }
    }
  });
});
