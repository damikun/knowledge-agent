import { useMutation, useQueryClient } from '@tanstack/react-query';
import { AppWindow, ChevronDown, Globe, Plus } from 'lucide-react';
import { useState } from 'react';
import { useNavigate } from 'react-router';
import { Button, type ButtonVariant } from '../../components/ui/Button';
import { ErrorMessage } from '../../components/ui/feedback';
import { Menu, type MenuItem } from '../../components/ui/Menu';
import { ConfirmDialog } from '../../components/ui/Modal';
import { toast } from '../../components/ui/Toast';
import { useReturnState } from '../../lib/returnTo';
import { useAgents } from './api';
import { widgetKeys, widgetsApi, useWidgets } from '../widgets/api';
import type { WidgetDto } from '../widgets/types';
import type { AgentDto } from './types';

export interface PutOnWebsiteProps {
  agent: AgentDto;
  variant?: ButtonVariant;
}

/** The widget editor reads the agent from navigation state, so a new widget starts with it chosen. */
export function PutOnWebsite({ agent, variant = 'secondary' }: PutOnWebsiteProps) {
  const returnState = useReturnState(agent.name);
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const widgets = useWidgets();
  const agents = useAgents();
  const [switching, setSwitching] = useState<WidgetDto | null>(null);
  const others = (widgets.data ?? []).filter((widget) => widget.agentId.toLowerCase() !== agent.id.toLowerCase());
  const currentAgent = switching ? agents.data?.find((item) => item.id.toLowerCase() === switching.agentId.toLowerCase()) : undefined;

  const assign = useMutation({
    mutationFn: (widget: WidgetDto) =>
      widgetsApi.updateWidget(widget.id, { name: widget.name, agentId: agent.id, enabled: widget.enabled, settings: widget.settings }),
    onSuccess: (saved) => {
      queryClient.setQueryData<WidgetDto[]>(widgetKeys.list, (old) => old?.map((item) => (item.id === saved.id ? saved : item)));
      void queryClient.invalidateQueries({ queryKey: widgetKeys.list });
      toast({ title: `${saved.name} now answers with ${agent.name}`, tone: 'success' });
      setSwitching(null);
    },
  });

  const items: MenuItem[] = [
    { label: 'Create a widget', icon: Plus, onSelect: () => navigate('/widgets/new', { state: { agentId: agent.id, ...returnState } }) },
    ...others.map((widget) => ({ label: `Use on ${widget.name}`, icon: AppWindow, onSelect: () => setSwitching(widget) })),
  ];

  return (
    <>
      <Menu
        label="Put it on your website"
        align="start"
        items={items}
        trigger={(props) => (
          <Button {...props} variant={variant} icon={Globe}>
            Put it on your website
            <ChevronDown className="size-4 text-current opacity-70" aria-hidden />
          </Button>
        )}
      />
      <ConfirmDialog
        open={switching !== null}
        tone="primary"
        title={`Use ${agent.name} on ${switching?.name ?? 'this widget'}?`}
        message={
          <>
            Visitors of {switching?.name} will talk to {agent.name}
            {currentAgent ? <> instead of {currentAgent.name}</> : null}. Conversations already running keep their agent.
          </>
        }
        confirmLabel="Use this agent"
        busy={assign.isPending}
        error={assign.isError ? <ErrorMessage error={assign.error} /> : null}
        onConfirm={() => switching && assign.mutate(switching)}
        onCancel={() => {
          assign.reset();
          setSwitching(null);
        }}
      />
    </>
  );
}
