import { focusManager, QueryClient } from '@tanstack/react-query';
import { act, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, onTestFinished, vi } from 'vitest';
import { authKeys } from '../features/auth/api';
import { adminUser, currentUser, supportUser } from '../test/apiFixtures';
import { fetchCalls, jsonResponse, mockFetch, problemResponse, renderRouter, signedOutResponse } from '../test/utils';
import { routes } from './routes';

afterEach(() => {
  vi.unstubAllGlobals();
  sessionStorage.clear();
});

/** Sign-in and sign-out are full-page navigations, so they are recorded instead of performed. */
function stubNavigation() {
  const assign = vi.fn();
  vi.stubGlobal('location', { ...window.location, assign });
  return assign;
}

function meCalls(fetchMock: ReturnType<typeof mockFetch>): number {
  return fetchCalls(fetchMock).filter((call) => call === 'GET /api/auth/me').length;
}

describe('AuthGate', () => {
  it('sends a visitor who is not signed in straight to sign-in, back to the page they opened', async () => {
    const assign = stubNavigation();
    const fetchMock = mockFetch({ 'GET /api/auth/me': () => signedOutResponse() });
    renderRouter(routes, { route: '/chat/c1?ref=mail#latest' });

    await waitFor(() => expect(assign).toHaveBeenCalledWith('/api/auth/login?returnUrl=%2Fchat%2Fc1%3Fref%3Dmail%23latest'));
    expect(screen.getByRole('status')).toHaveTextContent('Redirecting to sign-in');
    expect(screen.queryByRole('button', { name: 'Sign in' })).not.toBeInTheDocument();
    expect(screen.queryByRole('navigation', { name: 'Main' })).not.toBeInTheDocument();
    expect(fetchCalls(fetchMock)).toEqual(['GET /api/auth/me']);
  });

  it('shows the signed-out screen after signing out instead of signing straight back in', async () => {
    const assign = stubNavigation();
    mockFetch({ 'GET /api/auth/me': () => signedOutResponse() });
    sessionStorage.setItem('ka:signed-out', '1');
    renderRouter(routes, { route: '/' });

    expect(await screen.findByRole('alert')).toHaveTextContent('You are signed out.');
    expect(assign).not.toHaveBeenCalled();

    await userEvent.click(screen.getByRole('button', { name: 'Sign in' }));
    expect(assign).toHaveBeenCalledWith('/api/auth/login?returnUrl=%2F');
    expect(sessionStorage.getItem('ka:signed-out')).toBeNull();
  });

  it('stops redirecting when a sign-in it just started came back without a session', async () => {
    const assign = stubNavigation();
    mockFetch({ 'GET /api/auth/me': () => signedOutResponse() });
    sessionStorage.setItem('ka:sign-in-redirected-at', String(Date.now() - 2_000));
    renderRouter(routes, { route: '/chat' });

    expect(await screen.findByRole('alert')).toHaveTextContent('Sign-in did not complete');
    expect(assign).not.toHaveBeenCalled();
    expect(screen.getByRole('button', { name: 'Sign in' })).toBeInTheDocument();
  });

  it('shows why the last sign-in failed and does not return to the error', async () => {
    const assign = stubNavigation();
    mockFetch({ 'GET /api/auth/me': () => signedOutResponse() });
    renderRouter(routes, { route: '/?authError=The%20sign-in%20was%20cancelled.' });

    expect(await screen.findByRole('alert')).toHaveTextContent('Sign-in failed: The sign-in was cancelled.');
    await userEvent.click(screen.getByRole('button', { name: 'Sign in' }));
    expect(assign).toHaveBeenCalledWith('/api/auth/login?returnUrl=%2F');
  });

  it('checks the session again behind the sign-in screen and opens the app once signed in elsewhere', async () => {
    stubNavigation();
    sessionStorage.setItem('ka:signed-out', '1');
    let signedIn = false;
    let held: Promise<void> | null = null;
    const fetchMock = mockFetch({
      'GET /api/auth/me': async () => {
        await held;
        return signedIn ? jsonResponse(adminUser) : signedOutResponse();
      },
      'GET /api/conversations': [],
    });
    const { queryClient } = renderRouter(routes, { route: '/chat' });
    await screen.findByRole('button', { name: 'Sign in' });

    // Hold the /me check open to see what shows meanwhile.
    let release!: () => void;
    held = new Promise((resolve) => {
      release = resolve;
    });
    signedIn = true;
    const recheck = queryClient.refetchQueries({ queryKey: authKeys.me });
    await waitFor(() => expect(meCalls(fetchMock)).toBe(2));
    expect(queryClient.getQueryState(authKeys.me)?.status).toBe('success');
    await act(() => new Promise((resolve) => setTimeout(resolve, 0)));
    expect(screen.getByRole('button', { name: 'Sign in' })).toBeInTheDocument();
    expect(screen.queryByText('Loading Knowledge Agent')).not.toBeInTheDocument();

    release();
    await act(() => recheck);
    expect(await screen.findByRole('navigation', { name: 'Main' })).toBeInTheDocument();
  });

  it('shows a loading state, then the app', async () => {
    mockFetch({ 'GET /api/conversations': [] });
    renderRouter(routes, { route: '/chat' });

    expect(screen.getByRole('status')).toHaveTextContent('Loading Knowledge Agent');
    expect(await screen.findByRole('navigation', { name: 'Main' })).toBeInTheDocument();
    expect(await screen.findByRole('heading', { level: 1, name: 'Playground' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Sign in' })).not.toBeInTheDocument();
  });

  it('brings back the sign-in screen when a request answers 401 during the session', async () => {
    let signedIn = true;
    const fetchMock = mockFetch({
      'GET /api/auth/me': () => (signedIn ? jsonResponse(adminUser) : signedOutResponse()),
      'GET /api/conversations': () => {
        signedIn = false;
        return problemResponse({ title: 'Unauthorized' }, 401);
      },
    });
    renderRouter(routes, { route: '/chat' });

    expect(await screen.findByRole('button', { name: 'Sign in' })).toBeInTheDocument();
    expect(screen.getByRole('alert')).toHaveTextContent('Your session ended. Sign in again to continue.');
    expect(screen.queryByRole('navigation', { name: 'Main' })).not.toBeInTheDocument();
    expect(meCalls(fetchMock)).toBe(2);
  });

  it('brings back the sign-in screen when the chat stream answers 401', async () => {
    const user = userEvent.setup();
    let signedIn = true;
    const fetchMock = mockFetch({
      'GET /api/auth/me': () => (signedIn ? jsonResponse(adminUser) : signedOutResponse()),
      'GET /api/conversations': [],
      'POST /api/chat': () => {
        signedIn = false;
        return problemResponse({ title: 'Unauthorized' }, 401);
      },
    });
    renderRouter(routes, { route: '/chat' });

    await user.type(await screen.findByRole('textbox', { name: 'Message' }), 'How many vacation days do I get?');
    await user.keyboard('{Enter}');

    expect(await screen.findByRole('button', { name: 'Sign in' })).toBeInTheDocument();
    expect(fetchCalls(fetchMock)).toContain('POST /api/chat');
    expect(meCalls(fetchMock)).toBe(2);
  });

  it('asks who is signed in again after a 403, so a role taken away at the last session check shows at once', async () => {
    let signedIn = adminUser;
    const fetchMock = mockFetch({
      'GET /api/auth/me': () => jsonResponse(signedIn),
      'GET /api/prompts': () => {
        signedIn = supportUser;
        return problemResponse({ title: 'Forbidden' }, 403);
      },
    });
    renderRouter(routes, { route: '/prompts' });

    expect(await screen.findByRole('heading', { level: 1, name: 'You need the admin role to open this page' })).toBeInTheDocument();
    expect(screen.getByRole('navigation', { name: 'Main' })).toBeInTheDocument();
    expect(meCalls(fetchMock)).toBe(2);
  });

  it('offers a retry when the API cannot be reached, without checking again on every window focus', async () => {
    let up = false;
    const fetchMock = mockFetch({
      'GET /api/auth/me': () => (up ? jsonResponse(adminUser) : Promise.reject(new TypeError('Failed to fetch'))),
      'GET /api/conversations': [],
    });
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false, refetchOnWindowFocus: true } } });
    onTestFinished(() => focusManager.setFocused(undefined));
    renderRouter(routes, { route: '/chat', queryClient });

    expect(await screen.findByRole('alert')).toHaveTextContent('Cannot reach the API');
    expect(screen.queryByRole('button', { name: 'Sign in' })).not.toBeInTheDocument();

    // The query client reacts to focus asynchronously.
    await act(async () => {
      focusManager.setFocused(false);
      focusManager.setFocused(true);
      await new Promise((resolve) => setTimeout(resolve, 0));
    });
    expect(meCalls(fetchMock)).toBe(1);
    expect(screen.getByRole('button', { name: 'Try again' })).toBeInTheDocument();

    up = true;
    await userEvent.click(screen.getByRole('button', { name: 'Try again' }));
    expect(await screen.findByRole('navigation', { name: 'Main' })).toBeInTheDocument();
  });

  it('tells a signed-in user without a role to ask for one', async () => {
    const assign = stubNavigation();
    mockFetch({ 'GET /api/auth/me': currentUser({ name: 'Nora Newhire', email: 'nora@example.com', roles: [] }) });
    renderRouter(routes, { route: '/chat' });

    expect(await screen.findByText(/You are signed in as nora@example.com, but your account has no role/)).toBeInTheDocument();
    expect(screen.queryByRole('navigation', { name: 'Main' })).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Sign out' }));
    expect(assign).toHaveBeenCalledWith('/api/auth/logout');
  });
});
