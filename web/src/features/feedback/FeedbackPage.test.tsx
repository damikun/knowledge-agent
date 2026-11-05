import { chooseOption } from '../../test/select';
import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { agentDto, agentGuid, agentsRoutes, builtInAgent } from '../../test/agentsApi';
import { supportUser } from '../../test/apiFixtures';
import { feedbackDto, feedbackSummary, portalRating } from '../../test/feedbackApi';
import { CONVERSATION_ID } from '../../test/fixtures';
import { WIDGET_CONVERSATION_ID } from '../../test/inboxApi';
import { fetchCalls, mockFetch, renderWithProviders, requestUrls } from '../../test/utils';
import { widgetDto, widgetGuid, widgetsRoutes } from '../../test/widgetsApi';
import { FeedbackPage } from './FeedbackPage';

afterEach(() => {
  vi.unstubAllGlobals();
});

const SUMMARY = 'GET /api/feedback/summary';
const LIST = 'GET /api/feedback';

const routes = {
  [SUMMARY]: [feedbackSummary(), feedbackSummary({ widgetId: null, agentId: null, answers: 0, positive: 0, negative: 0, positiveRatio: null, ratings: 1, averageRating: 5, agentRecorded: 0 })],
  [LIST]: [feedbackDto(), portalRating],
  ...widgetsRoutes(() => [widgetDto()]),
  ...agentsRoutes(() => [builtInAgent, agentDto()]),
};

describe('FeedbackPage', () => {
  it('summarizes the feedback per widget and agent', async () => {
    mockFetch(routes);
    renderWithProviders(<FeedbackPage />);

    expect(await screen.findByRole('heading', { level: 1, name: 'Feedback' })).toBeInTheDocument();
    const shop = await screen.findByRole('region', { name: 'Shop help' });
    expect(await within(shop).findByText('Website helper')).toBeInTheDocument();
    expect(within(shop).getByText('75%')).toBeInTheDocument();
    expect(within(shop).getByText('15 up, 5 down')).toBeInTheDocument();
    expect(within(shop).getByText('4.3/5')).toBeInTheDocument();
    expect(within(shop).getByText('From 4 ratings')).toBeInTheDocument();

    const portal = screen.getByRole('region', { name: 'Portal chat' });
    expect(within(portal).getByText('Knowledge Agent')).toBeInTheDocument();
    expect(within(portal).getByText('No answer ratings')).toBeInTheDocument();
    expect(within(portal).getByText('5.0/5')).toBeInTheDocument();
  });

  it('lists the latest feedback and links each to its conversation', async () => {
    mockFetch(routes);
    renderWithProviders(<FeedbackPage />);

    const list = await screen.findByRole('list', { name: 'Feedback' });
    const [widgetItem, portalItem] = within(list).getAllByRole('listitem');
    expect(widgetItem).toHaveTextContent('Not helpful');
    expect(widgetItem).toHaveTextContent('Answer rating');
    expect(widgetItem).toHaveTextContent('By a visitor');
    expect(widgetItem).toHaveTextContent('It did not answer my question.');
    expect(widgetItem).toHaveTextContent('Incomplete');
    expect(widgetItem).toHaveTextContent('Worn shoes cannot be returned');
    expect(within(widgetItem!).getByRole('link', { name: 'Return an order' })).toHaveAttribute('href', `/inbox/${WIDGET_CONVERSATION_ID}`);

    expect(portalItem).toHaveTextContent('5 of 5');
    expect(portalItem).toHaveTextContent('Chat rating');
    expect(portalItem).toHaveTextContent('By staff');
    expect(within(portalItem!).getByRole('link', { name: 'Vacation policy' })).toHaveAttribute('href', `/chat/${CONVERSATION_ID}`);
    expect(portalItem).toHaveTextContent('Portal chat · Knowledge Agent');
  });

  it('filters the feedback and changes the period of both the summary and the list', async () => {
    const user = userEvent.setup();
    const fetchMock = mockFetch(routes);
    renderWithProviders(<FeedbackPage />);
    await screen.findByRole('list', { name: 'Feedback' });
    expect(requestUrls(fetchMock, SUMMARY)[0]!.searchParams.get('days')).toBe('30');
    expect(requestUrls(fetchMock, LIST)[0]!.searchParams.get('take')).toBe('100');

    await user.click(within(screen.getByRole('radiogroup', { name: 'Period' })).getByRole('radio', { name: '7 days' }));
    await waitFor(() => expect(requestUrls(fetchMock, SUMMARY).at(-1)!.searchParams.get('days')).toBe('7'));
    expect(requestUrls(fetchMock, LIST).at(-1)!.searchParams.get('days')).toBe('7');

    await chooseOption(user, screen.getByRole('combobox', { name: 'Kind' }), 'Answer ratings');
    await chooseOption(user, screen.getByRole('combobox', { name: 'Rating' }), 'Negative');
    await chooseOption(user, await screen.findByRole('combobox', { name: 'Widget' }), 'Shop help');
    await chooseOption(user, screen.getByRole('combobox', { name: 'Agent' }), 'Website helper');
    await waitFor(() => expect(requestUrls(fetchMock, LIST).at(-1)!.searchParams.get('agentId')).toBe(agentGuid(2)));
    const last = requestUrls(fetchMock, LIST).at(-1)!.searchParams;
    expect(Object.fromEntries(last)).toEqual({ widgetId: widgetGuid(1), agentId: agentGuid(2), kind: 'answer', positive: 'false', days: '7', take: '100' });
  });

  it('shows support users widget ids, as they cannot read the widgets', async () => {
    const fetchMock = mockFetch({ ...routes, 'GET /api/auth/me': supportUser });
    renderWithProviders(<FeedbackPage />);

    expect(await screen.findByRole('region', { name: 'Widget 0199c1e2…0001' })).toBeInTheDocument();
    expect(fetchCalls(fetchMock)).not.toContain('GET /api/widgets');
  });
});
