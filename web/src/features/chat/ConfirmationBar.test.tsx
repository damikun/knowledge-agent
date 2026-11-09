import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import type { ToolCallView } from './chatReducer';
import { ConfirmationBar } from './ConfirmationBar';

const call: ToolCallView = {
  callId: 'm1',
  toolName: 'mcp_shop__refund_order',
  agent: 'orchestrator',
  parentCallId: null,
  arguments: { order_id: '1042', amount: 20 },
  status: 'success',
  summary: 'Waiting for confirmation',
  durationMs: 3,
  citations: [],
  children: [],
  subAgent: null,
  confirmation: {
    status: 'pending',
    title: 'Refund order',
    connection: 'Shop',
    expiresAt: '2099-01-01T00:00:00Z',
    details: [
      { label: 'order_id', value: '1042' },
      { label: 'amount', value: '20' },
    ],
  },
};

describe('ConfirmationBar', () => {
  it('shows what the action will run with, and answers with the call id', async () => {
    const user = userEvent.setup();
    const onAnswer = vi.fn();
    render(<ConfirmationBar calls={[call]} onAnswer={onAnswer} />);

    const bar = screen.getByRole('region', { name: 'Actions to confirm' });
    expect(within(bar).getByText('Refund order')).toBeInTheDocument();
    expect(within(bar).getByText('order_id:')).toBeInTheDocument();
    expect(within(bar).getByText('1042')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Confirm Refund order' }));
    expect(onAnswer).toHaveBeenCalledWith('m1', true);
  });
});
