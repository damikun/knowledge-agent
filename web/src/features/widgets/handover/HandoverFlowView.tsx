import { Bot, CircleCheck, Clock, Hash, Hourglass, Inbox, Mail, MessageCircleQuestion, Undo2, type LucideIcon } from 'lucide-react';
import { useCallback, useMemo } from 'react';
import { useNavigate } from 'react-router';
import type { AgentDto } from '../../agents/types';
import { useAlerts } from '../../alerts';
import type { IntegrationsDto } from '../../notifications/types';
import { FlowCanvas } from '../../../components/flow/FlowCanvas';
import { graphSize } from '../../../components/flow/graph';
import { GraphPanel } from '../../../components/flow/GraphPanel';
import { buildHandoverFlow, type HandoverDraft, type HandoverStep } from './handoverSteps';
import { layoutRows } from '../../../components/flow/rowsLayout';
import { useReturnState } from '../../../lib/returnTo';

const STEP_WIDTH = 168;

function iconOf(step: HandoverStep): LucideIcon {
  switch (step.kind) {
    case 'visitor':
      return MessageCircleQuestion;
    case 'agent':
      return Bot;
    case 'hours':
      return Clock;
    case 'channel':
      return step.id === 'slack' ? Hash : step.id === 'email' ? Mail : Inbox;
    case 'wait':
      return Hourglass;
    case 'joined':
      return CircleCheck;
    default:
      return Undo2;
  }
}

const actionOf = (step: HandoverStep) => (step.href ? (step.kind === 'agent' ? 'Opens the agent editor' : step.kind === 'channel' && step.id === 'inbox' ? 'Opens the Inbox' : 'Opens Connections') : 'Set in this panel');

export interface HandoverFlowProps {
  draft: HandoverDraft;
  agents: readonly AgentDto[];
  integrations: IntegrationsDto | null | undefined;
  className?: string;
}

function HandoverFlowView({ draft, agents, integrations, className }: HandoverFlowProps) {
  const alerts = useAlerts();
  const navigate = useNavigate();
  // The editor builds a new draft object on every render, so compare by content.
  const draftKey = JSON.stringify(draft);
  const stableDraft = useMemo(() => draft, [draftKey]);
  const flow = useMemo(() => buildHandoverFlow(stableDraft, { agents, integrations, alerts: alerts.data?.alerts ?? [] }), [stableDraft, agents, integrations, alerts.data]);
  const positioned = useMemo(() => layoutRows(flow.rows, { width: STEP_WIDTH, rowGap: 28, columnGap: 12 }), [flow]);
  const returnState = useReturnState();
  const onActivate = useCallback((step: HandoverStep) => (step.href ? void navigate(step.href, { state: returnState }) : undefined), [navigate, returnState]);

  return (
    <GraphPanel
      title="When a visitor asks for a person"
      description="How a request reaches your team with these settings, before you save."
      naturalSize={graphSize(positioned)}
      maxHeight={480}
      className={className}
    >
      {(large) => (
        <FlowCanvas
          label="When a visitor asks for a person"
          positioned={positioned}
          edges={flow.edges}
          direction="TB"
          iconOf={iconOf}
          onActivate={onActivate}
          actionOf={actionOf}
          nodeRole="link"
          interactive={large}
        />
      )}
    </GraphPanel>
  );
}

export default HandoverFlowView;
