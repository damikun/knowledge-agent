import { screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { renderWithProviders } from '../../test/utils';
import type { SourceStatsDto } from '../sources/types';
import { EmptyChat } from './EmptyChat';

function stats(overrides: Partial<SourceStatsDto>): SourceStatsDto {
  return { total: 0, ready: 0, pending: 0, processing: 0, failed: 0, waiting: 0, readyChunks: 0, readyByType: {}, readyByEmbeddingModel: {}, ...overrides };
}

const HINTS = ['No sources yet', 'Your sources are being processed', 'No sources are ready'];

function hint(sourceStats: SourceStatsDto | undefined): string | null {
  const { unmount } = renderWithProviders(<EmptyChat sourceStats={sourceStats} onPick={() => undefined} />);
  expect(screen.getByRole('heading', { name: 'Ask your knowledge base' })).toBeInTheDocument();
  const shown = HINTS.filter((title) => screen.queryByText(title) !== null);
  unmount();
  expect(shown.length).toBeLessThanOrEqual(1);
  return shown[0] ?? null;
}

describe('EmptyChat source hint', () => {
  it('derives the hint from the source stats', () => {
    expect(hint(stats({ total: 0 }))).toBe('No sources yet');
    expect(hint(stats({ total: 3, pending: 1, processing: 2 }))).toBe('Your sources are being processed');
    expect(hint(stats({ total: 2, failed: 2 }))).toBe('No sources are ready');
  });

  it('shows no hint while the stats load or once a source is ready', () => {
    expect(hint(undefined)).toBeNull();
    expect(hint(stats({ total: 1240, ready: 1, pending: 1239 }))).toBeNull();
  });

  it('links to the Sources page when there is nothing to ask about', () => {
    renderWithProviders(<EmptyChat sourceStats={stats({ total: 0 })} onPick={() => undefined} />);
    expect(screen.getByRole('link', { name: 'Sources' })).toHaveAttribute('href', '/sources');
  });

  it('sends sources that wait for an embedding model to the Models page instead of reporting a failure', () => {
    renderWithProviders(<EmptyChat sourceStats={stats({ total: 3, failed: 1, waiting: 2 })} onPick={() => undefined} />);
    expect(screen.getByText('No sources are ready')).toBeInTheDocument();
    const link = screen.getByRole('link', { name: 'AI models page' });
    expect(link).toHaveAttribute('href', '/models#providers');
    expect(link.parentElement).toHaveTextContent(
      '2 sources wait for an embedding model. Connect one on the AI models page and they are queued automatically.',
    );
    expect(screen.queryByText(/Ingestion failed/)).toBeNull();
  });
});
