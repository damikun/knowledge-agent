import { screen, within } from '@testing-library/react';
import { afterAll, beforeAll, describe, expect, it } from 'vitest';
import { agentDto } from '../../../test/agentsApi';
import { embeddingProfile, orchestratorProfile } from '../../../test/apiFixtures';
import { workingEmail, workingSlack } from '../../../test/integrationsApi';
import { mockFetch, renderWithProviders } from '../../../test/utils';
import { widgetDto, widgetSettings } from '../../../test/widgetsApi';
import { HandoverFlow } from './HandoverFlow';

// jsdom has no ResizeObserver.
class NoopResizeObserver {
  observe() {}
  unobserve() {}
  disconnect() {}
}

// Not vi.stubGlobal: the lazy graph can still measure after the test, when stubs are already undone.
const original = globalThis.ResizeObserver;
beforeAll(() => {
  globalThis.ResizeObserver = NoopResizeObserver as unknown as typeof ResizeObserver;
});

afterAll(() => {
  globalThis.ResizeObserver = original;
});

const shop = widgetDto({ settings: widgetSettings({ behavior: { handoverEnabled: true }, handover: { channel: 'slack' } }) });

function serve() {
  return mockFetch({
    'GET /api/widgets': [shop],
    'GET /api/sources/folders': { folders: [], unfiledCount: 3, totalCount: 3 },
    'GET /api/sources/stats': { total: 3, ready: 3, pending: 0, processing: 0, failed: 0, waiting: 0, readyChunks: 40, readyByType: {}, readyByEmbeddingModel: {} },
    'GET /api/sources': { items: [], total: 0, offset: 0, limit: 100, nextOffset: null },
    'GET /api/integrations': { email: workingEmail, slack: workingSlack },
    'GET /api/models/profiles': [orchestratorProfile('openai'), embeddingProfile()],
    'GET /api/alerts': { alerts: [], snoozed: 0 },
  });
}

describe('HandoverFlow', () => {
  it('draws what happens when a visitor asks for a person', { timeout: 15_000 }, async () => {
    serve();
    renderWithProviders(
      <HandoverFlow
        draft={{
          handoverEnabled: true,
          agentId: agentDto().id,
          channel: 'slack',
          emailTo: [],
          slackChannelId: '',
          escalateAfterMinutes: 10,
          waitMinutes: 15,
          alwaysAvailable: true,
          timeZone: 'UTC',
          openDays: 0,
          contactCapture: 'never',
        }}
        agents={[agentDto()]}
        integrations={{ email: workingEmail, slack: workingSlack }}
      />,
    );
    const graph = await screen.findByRole('region', { name: 'When a visitor asks for a person' });
    // The lazy graph can be slow on a busy CI runner.
    expect(await within(graph).findByText('Website helper', {}, { timeout: 8_000 })).toBeInTheDocument();
    expect(within(graph).getByText('Waits 15 minutes')).toBeInTheDocument();
    expect(within(graph).getByText('Email after 10 minutes without a reply')).toBeInTheDocument();
    expect(within(graph).getByText('Nobody joins')).toBeInTheDocument();
  });
});
