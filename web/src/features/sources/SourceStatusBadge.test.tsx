import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { sourceDto } from '../../test/fixtures';
import { SourceStatusBadge } from './SourceStatusBadge';
import type { IngestionStage, SourceDto, SourceStatus } from './types';

const renderBadge = (overrides: Partial<SourceDto>) => render(<SourceStatusBadge source={sourceDto(overrides)} />);

describe('SourceStatusBadge', () => {
  it.each([
    ['pending', 'Pending', true],
    ['processing', 'Processing', true],
    ['ready', 'Ready', false],
    ['failed', 'Failed', false],
    ['waiting', 'Waiting for a model', false],
  ])('renders %s as "%s" (animated: %s)', (status, label, animated) => {
    renderBadge({ status: status as SourceStatus });
    const badge = screen.getByText(label);
    expect(badge).toHaveAttribute('data-status', status);
    if (animated) {
      expect(badge).toHaveAttribute('data-animated', 'true');
      expect(badge.querySelector('.animate-pulse, .animate-spin')).not.toBeNull();
    } else {
      expect(badge).not.toHaveAttribute('data-animated');
      expect(badge.querySelector('.animate-pulse, .animate-spin')).toBeNull();
    }
  });

  it('accepts statuses in any casing', () => {
    renderBadge({ status: 'Processing' as SourceStatus });
    expect(screen.getByText('Processing')).toHaveAttribute('data-status', 'processing');
  });

  it('exposes the ingestion error of a failed source as its tooltip', () => {
    renderBadge({ status: 'failed', error: 'Download exceeded 20 MB' });
    expect(screen.getByText('Failed')).toHaveAttribute('title', 'Download exceeded 20 MB');
  });

  it('shows a source waiting for an embedding model in amber and says why, also to screen readers', () => {
    const error = "No configured embedding provider is available for the 'embedding' profile (Provider = auto).";
    renderBadge({ status: 'waiting', error, chunkCount: 0, embeddedChunks: 0, embeddingModel: null });
    const badge = screen.getByText('Waiting for a model');
    expect(badge).toHaveAttribute('data-status', 'waiting');
    expect(badge).toHaveAttribute('data-tone', 'warning');
    expect(badge).toHaveAttribute('title', error);
    expect(badge).toHaveTextContent(`Waiting for a model: ${error}`);
  });

  it('describes a waiting source without an error', () => {
    renderBadge({ status: 'waiting', error: null });
    const badge = screen.getByText('Waiting for a model');
    expect(badge).toHaveAttribute('title', 'Waiting for an embedding model');
    expect(badge).toHaveTextContent(/^Waiting for a model$/);
  });

  it('shows unknown statuses verbatim', () => {
    renderBadge({ status: 'archived' as SourceStatus });
    expect(screen.getByText('archived')).toBeInTheDocument();
  });

  it.each([
    ['fetching', 'Fetching'],
    ['extracting', 'Extracting'],
    ['chunking', 'Chunking'],
    ['contextualizing', 'Adding context'],
    ['storing', 'Storing'],
  ])('names the %s stage of a processing source', (stage, label) => {
    renderBadge({ status: 'processing', stage: stage as IngestionStage, chunkCount: 400, embeddedChunks: 400 });
    expect(screen.getByText(label)).toHaveAttribute('data-status', 'processing');
    expect(screen.queryByRole('progressbar')).toBeNull();
  });

  it('counts the embedded chunks and shows them as a progress bar', () => {
    renderBadge({ status: 'processing', stage: 'embedding', chunkCount: 4000, embeddedChunks: 1250 });
    expect(screen.getByText('Embedding 1,250 of 4,000')).toHaveAttribute('data-status', 'processing');
    const bar = screen.getByRole('progressbar', { name: 'Chunks embedded' });
    expect(bar).toHaveAttribute('aria-valuenow', '1250');
    expect(bar).toHaveAttribute('aria-valuemax', '4000');
    expect(bar.firstElementChild).toHaveStyle({ width: '31.25%' });
  });

  it('leaves out the count and the bar without a chunk total', () => {
    renderBadge({ status: 'processing', stage: 'embedding', chunkCount: 0, embeddedChunks: 0 });
    expect(screen.getByText('Embedding')).toBeInTheDocument();
    expect(screen.queryByRole('progressbar')).toBeNull();
  });

  it('shows the status when the stage is unknown or the source is not processing', () => {
    renderBadge({ status: 'processing', stage: 'indexing' as IngestionStage });
    renderBadge({ status: 'ready', stage: 'storing' });
    expect(screen.getByText('Processing')).toBeInTheDocument();
    expect(screen.getByText('Ready')).toBeInTheDocument();
  });
});
