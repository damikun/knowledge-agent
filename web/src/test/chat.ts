import { screen, within } from '@testing-library/react';
import type { UserEvent } from '@testing-library/user-event';
import { expect, vi } from 'vitest';
import type { ConversationLabelDto, ConversationSummaryDto, LabelDto } from '../features/chat/types';
import { conversationSummaryDto } from './fixtures';

/** The chat shows its list beside the thread from 768 px; jsdom matches no media query, so tests ask for a wide window. */
export function stubWideScreen(): void {
  vi.stubGlobal('matchMedia', (query: string) => ({
    matches: query.includes('min-width'),
    media: query,
    onchange: null,
    addEventListener: () => undefined,
    removeEventListener: () => undefined,
    addListener: () => undefined,
    removeListener: () => undefined,
    dispatchEvent: () => false,
  }));
}

/** Opens the Details of an answer, as admins can, and checks its text. */
export async function expectAnswerDetails(user: UserEvent, answer: HTMLElement, text: string): Promise<void> {
  await user.click(within(answer).getByRole('button', { name: 'Details' }));
  expect(await screen.findByRole('dialog', { name: 'Answer details' })).toHaveTextContent(text);
  await user.keyboard('{Escape}');
}

/** Picks an option of the shared Select whose name matches. */
export async function pickOption(user: UserEvent, combobox: HTMLElement, name: RegExp): Promise<void> {
  await user.click(combobox);
  await user.click(await screen.findByRole('option', { name }));
}

export const billingLabel: LabelDto = { id: 'label-billing', name: 'Billing', color: 'amber', description: null, autoApply: false, conversationCount: 3 };
export const refundsLabel: LabelDto = { id: 'label-refunds', name: 'Refunds', color: 'purple', description: 'Refund questions', autoApply: true, conversationCount: 1 };

export function labelOn(label: LabelDto, source: ConversationLabelDto['source'] = 'manual'): ConversationLabelDto {
  return { id: label.id, name: label.name, color: label.color, source };
}

/** A conversation in the list shape of the API with search and labels. */
export function listItem(overrides: Partial<ConversationSummaryDto> = {}): ConversationSummaryDto {
  return conversationSummaryDto({ pinned: false, labels: [], ...overrides });
}
