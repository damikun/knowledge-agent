import { act, cleanup, render, screen } from '@testing-library/react';
import { StrictMode } from 'react';
import { createMemoryRouter, Outlet, RouterProvider } from 'react-router';
import { afterEach, beforeEach, describe, expect, it, onTestFinished, vi } from 'vitest';

const moduleLoadError = new TypeError('Failed to fetch dynamically imported module: http://localhost:5173/src/features/models/ModelsPage.tsx');
const waitingLine = "The server isn't responding. The page reloads when it's back.";

function stubReload() {
  const reload = vi.fn();
  vi.stubGlobal('location', { ...window.location, reload });
  return reload;
}

// Until the test brings the server up, the reachability check fails like a refused connection.
function stubServer({ up }: { up: boolean }) {
  const server = {
    up,
    check: vi.fn(async () => {
      if (!server.up) {
        throw new TypeError('Failed to fetch');
      }
      return new Response(null, { status: 200 });
    }),
  };
  vi.stubGlobal('fetch', server.check);
  return server;
}

function advance(ms: number) {
  return act(() => vi.advanceTimersByTimeAsync(ms));
}

// Fresh modules stand for a fresh page load; session storage survives it as it survives a reload.
async function openFailingPage(error: Error, { production = false } = {}) {
  vi.resetModules();
  const { RouteErrorPage } = await import('./RouteErrorPage');
  const { recoverOnPreloadError } = await import('./moduleReload');
  onTestFinished(recoverOnPreloadError());
  const lazy = () => {
    // Production builds wrap dynamic imports in Vite's preload helper, which reports the failure first.
    if (production) {
      window.dispatchEvent(Object.assign(new Event('vite:preloadError', { cancelable: true }), { payload: error }));
    }
    return Promise.reject(error);
  };
  const router = createMemoryRouter([
    {
      path: '/',
      element: <Outlet />,
      errorElement: <RouteErrorPage />,
      children: [
        { index: true, element: <p>Chat</p> },
        { path: 'models', lazy },
      ],
    },
  ]);
  render(
    <StrictMode>
      <RouterProvider router={router} />
    </StrictMode>,
  );
  await act(() => router.navigate('/models'));
  await advance(0);
}

beforeEach(() => {
  vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
});

afterEach(() => {
  vi.useRealTimers();
  sessionStorage.clear();
});

describe('RouteErrorPage', () => {
  it('reloads once when the code of a page fails to load and the server answers', async () => {
    const reload = stubReload();
    const server = stubServer({ up: true });
    await openFailingPage(moduleLoadError);

    expect(server.check).toHaveBeenCalledWith('/', expect.objectContaining({ method: 'HEAD', cache: 'no-store' }));
    expect(reload).toHaveBeenCalledOnce();
    expect(screen.getByRole('status')).toHaveTextContent('Loading Knowledge Agent');
    expect(screen.queryByText('Something went wrong')).not.toBeInTheDocument();
  });

  it('waits while the server does not answer and reloads once it is back', async () => {
    const reload = stubReload();
    const server = stubServer({ up: false });
    await openFailingPage(moduleLoadError);

    expect(screen.getByText(moduleLoadError.message)).toBeInTheDocument();
    expect(screen.getByRole('status')).toHaveTextContent(waitingLine);
    expect(screen.getByRole('button', { name: 'Reload the page' })).toBeInTheDocument();

    const checks = server.check.mock.calls.length;
    await advance(6_000);
    expect(server.check).toHaveBeenCalledTimes(checks + 3);
    expect(reload).not.toHaveBeenCalled();

    server.up = true;
    await advance(2_000);
    expect(reload).toHaveBeenCalledOnce();
    await advance(10_000);
    expect(server.check).toHaveBeenCalledTimes(checks + 4);
    expect(reload).toHaveBeenCalledOnce();
  });

  it('runs one check at a time when both the preload helper and the error page see the failure', async () => {
    const reload = stubReload();
    const server = stubServer({ up: false });
    await openFailingPage(moduleLoadError, { production: true });

    const checks = server.check.mock.calls.length;
    await advance(4_000);
    expect(server.check).toHaveBeenCalledTimes(checks + 2);

    server.up = true;
    await advance(2_000);
    expect(reload).toHaveBeenCalledOnce();
  });

  it('shows the error and stops checking when the page fails again right after the reload', async () => {
    const reload = stubReload();
    const server = stubServer({ up: true });
    await openFailingPage(moduleLoadError);
    cleanup();
    await openFailingPage(moduleLoadError);

    expect(screen.getByText('Something went wrong')).toBeInTheDocument();
    expect(screen.getByText(moduleLoadError.message)).toBeInTheDocument();
    expect(screen.queryByText(waitingLine)).not.toBeInTheDocument();
    const checks = server.check.mock.calls.length;
    await advance(10_000);
    expect(server.check).toHaveBeenCalledTimes(checks);
    expect(reload).toHaveBeenCalledOnce();
  });

  it('stops checking when the error page goes away', async () => {
    const reload = stubReload();
    const server = stubServer({ up: false });
    await openFailingPage(moduleLoadError);
    cleanup();

    const checks = server.check.mock.calls.length;
    server.up = true;
    await advance(10_000);
    expect(server.check).toHaveBeenCalledTimes(checks);
    expect(reload).not.toHaveBeenCalled();
  });

  it('shows other errors without checking the server or reloading', async () => {
    const reload = stubReload();
    const server = stubServer({ up: true });
    await openFailingPage(new ReferenceError('process is not defined'));

    expect(screen.getByText('Something went wrong')).toBeInTheDocument();
    expect(screen.getByText('process is not defined')).toBeInTheDocument();
    expect(server.check).not.toHaveBeenCalled();
    expect(reload).not.toHaveBeenCalled();
  });
});
