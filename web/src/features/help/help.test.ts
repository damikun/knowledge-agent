import { afterEach, describe, expect, it } from 'vitest';
import { portalPath, toHelpStatus } from './api';
import { clearThread, loadThread, saveThread } from './storage';
import { helpSuggestions, pageSegment } from './suggestions';
import type { HelpSetupDto, HelpTurn } from './types';

const done: HelpSetupDto = { chatModel: true, embeddingModel: true, sources: true, agent: true, widget: true };

function turn(overrides: Partial<HelpTurn> = {}): HelpTurn {
  return { id: 't1', question: 'How?', text: 'Like this [S1].', citations: [], error: null, ...overrides };
}

afterEach(() => {
  window.sessionStorage.clear();
});

describe('helpSuggestions', () => {
  it('puts the next setup step first for admins, then the questions of the page', () => {
    const questions = helpSuggestions('/sources/abc', { ...done, chatModel: false }, true);

    expect(questions[0]).toBe('How do I connect a model?');
    expect(questions).toContain('How do I add a website?');
    expect(questions.length).toBeLessThanOrEqual(4);
  });

  it('offers no setup steps to support users and keeps them to their own pages', () => {
    expect(helpSuggestions('/inbox/123', { ...done, widget: false }, false)).toEqual([
      'How do I take over a conversation?',
      'How do I hand a conversation back to the assistant?',
      'How do labels work?',
      'How do I save a segment?',
    ]);
    expect(helpSuggestions('/models', null, false)).not.toContain('How do I add an API key?');
  });

  it('falls back to general questions on an unknown page and reads the first path segment', () => {
    expect(helpSuggestions('/nowhere', done, true)).toContain('What are the setup steps?');
    expect(pageSegment('/Agents/new?step=2')).toBe('agents');
    expect(pageSegment('/')).toBe('');
  });
});

describe('help storage', () => {
  it('keeps the conversation in session storage only and drops what it cannot read', () => {
    saveThread({ sessionId: 's1', turns: [turn(), { broken: true } as unknown as HelpTurn, turn({ id: 't2', text: '' })] });

    expect(window.localStorage.length).toBe(0);
    expect(loadThread()).toEqual({ sessionId: 's1', turns: [turn()] });

    clearThread();
    expect(loadThread()).toEqual({ sessionId: null, turns: [] });

    window.sessionStorage.setItem('ka-help-thread', '{not json');
    expect(loadThread()).toEqual({ sessionId: null, turns: [] });
  });
});

describe('help api mapping', () => {
  it('keeps links inside the portal', () => {
    expect(portalPath('/models#providers')).toBe('/models#providers');
    expect(portalPath('//evil.example')).toBeNull();
    expect(portalPath('https://evil.example')).toBeNull();
    expect(portalPath('javascript:alert(1)')).toBeNull();
  });

  it('reads a missing or odd status as off', () => {
    expect(toHelpStatus(null)).toEqual({ enabled: false, canManage: false, modelReady: false, setup: null });
    expect(toHelpStatus({ enabled: true, canManage: 'yes', modelReady: true, setup: { chatModel: true } }).setup).toEqual({
      chatModel: true,
      embeddingModel: false,
      sources: false,
      agent: false,
      widget: false,
    });
  });
});
