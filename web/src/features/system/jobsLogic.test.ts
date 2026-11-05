import { describe, expect, it } from 'vitest';
import { describeCron, jobKindDescription, jobKindLabel } from './jobs';

describe('job kinds', () => {
  it('labels the known kinds and shows unknown kinds as they are', () => {
    expect(jobKindLabel('ingestion.source')).toBe('Source ingestion');
    expect(jobKindLabel('ingestion.refresh-web')).toBe('Web source refresh');
    expect(jobKindLabel('evaluations.run')).toBe('Evaluation run');
    expect(jobKindLabel('reports.export')).toBe('reports.export');
    expect(jobKindDescription('ingestion.refresh-web')).not.toBeNull();
    expect(jobKindDescription('reports.export')).toBeNull();
  });

  it('names every scheduled kind the API registers', () => {
    expect(['conversations.close-idle', 'handover.mail-poll', 'widgets.retention'].map(jobKindLabel)).toEqual([
      'Close quiet conversations',
      'Email replies',
      'Widget data cleanup',
    ]);
  });
});

describe('describeCron', () => {
  it('puts the common schedules into words', () => {
    expect(describeCron('* * * * *')).toEqual({ text: 'Every minute', atTime: false });
    expect(describeCron('*/5 * * * *')).toEqual({ text: 'Every 5 minutes', atTime: false });
    expect(describeCron('*/1 * * * *')).toEqual({ text: 'Every minute', atTime: false });
    expect(describeCron('15 * * * *')).toEqual({ text: 'Every hour at :15', atTime: true });
    expect(describeCron('0 4 * * *')).toEqual({ text: 'Every day at 04:00', atTime: true });
    expect(describeCron(' 20 3 * * * ')).toEqual({ text: 'Every day at 03:20', atTime: true });
  });

  it('leaves other expressions to be shown as they are', () => {
    expect(describeCron('0 4 * * 1')).toBeNull();
    expect(describeCron('0 4 1 * *')).toBeNull();
    expect(describeCron('0 */2 * * *')).toBeNull();
    expect(describeCron('0 4 * *')).toBeNull();
    expect(describeCron('@daily')).toBeNull();
  });
});
