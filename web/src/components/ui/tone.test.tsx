import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { WifiOff } from 'lucide-react';
import { describe, expect, it, vi } from 'vitest';
import { axeViolations } from '../../test/a11y';
import { Badge } from './Badge';
import { Banner } from './Banner';
import { Alert } from './feedback';
import { StatusDot } from './StatusDot';
import { TONES, type Tone } from './tone';

const tones = Object.keys(TONES) as Tone[];

describe('tones', () => {
  it.each(tones)('%s looks the same on a badge, an alert and a status dot', (tone) => {
    render(
      <>
        <Badge tone={tone}>Badge</Badge>
        <Alert tone={tone} title="Alert" live />
        <StatusDot tone={tone} label="Dot" />
      </>,
    );

    const badge = screen.getByText('Badge');
    const alert = screen.getByRole('alert');
    const dot = screen.getByRole('img', { name: 'Dot' });
    expect([badge, alert, dot].map((element) => element.getAttribute('data-tone'))).toEqual([tone, tone, tone]);
    expect(badge).toHaveClass(TONES[tone].subtle, TONES[tone].text);
    expect(alert).toHaveClass(TONES[tone].subtle);
    expect(dot).toHaveClass(TONES[tone].solid);
  });

  it('reads the older tone names as the shared ones', () => {
    render(
      <>
        <Badge tone="accent">Admin</Badge>
        <Badge tone="outline">v4</Badge>
        <Alert tone="accent" title="Heads up" live />
      </>,
    );

    expect(screen.getByText('Admin')).toHaveAttribute('data-tone', 'info');
    expect(screen.getByText('v4')).toHaveAttribute('data-tone', 'neutral');
    expect(screen.getByRole('alert')).toHaveAttribute('data-tone', 'info');
  });
});

describe('Badge', () => {
  it('shows a dot in the tone only on request', () => {
    render(
      <>
        <Badge tone="success" dot>
          Ready
        </Badge>
        <Badge tone="success">Enabled</Badge>
      </>,
    );

    expect(screen.getByText('Ready').firstElementChild).toHaveClass(TONES.success.solid);
    expect(screen.getByText('Enabled').firstElementChild).toBeNull();
  });
});

describe('StatusDot', () => {
  it('is hidden from screen readers without a label', () => {
    const { container } = render(<StatusDot tone="warning" pulse />);
    const dot = container.firstElementChild!;
    expect(dot).toHaveAttribute('aria-hidden', 'true');
    expect(dot).toHaveAttribute('data-tone', 'warning');
    expect(dot).toHaveClass('animate-pulse');
  });
});

describe('Banner', () => {
  it('announces one problem with its action', async () => {
    const onRetry = vi.fn();
    render(
      <Banner
        tone="danger"
        icon={WifiOff}
        action={
          <button type="button" onClick={onRetry}>
            Retry
          </button>
        }
      >
        Cannot reach the API.
      </Banner>,
    );

    const banner = screen.getByRole('status');
    expect(banner).toHaveAttribute('data-tone', 'danger');
    expect(banner).toHaveClass(TONES.danger.subtle, TONES.danger.text);
    expect(banner).toHaveTextContent('Cannot reach the API.');
    await userEvent.click(screen.getByRole('button', { name: 'Retry' }));
    expect(onRetry).toHaveBeenCalledOnce();
    expect(await axeViolations()).toEqual([]);
  });
});
