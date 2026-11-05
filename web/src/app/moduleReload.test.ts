import { afterEach, beforeEach, describe, expect, it, onTestFinished, vi } from 'vitest';

const chromiumError = 'Failed to fetch dynamically imported module: http://localhost:5173/src/features/models/ModelsPage.tsx';

// Fresh modules stand for a fresh page load; session storage survives it as it survives a reload.
async function loadPage() {
  vi.resetModules();
  return import('./moduleReload');
}

function stubReload() {
  const reload = vi.fn();
  vi.stubGlobal('location', { ...window.location, reload });
  return reload;
}

// A null status makes the reachability check fail like a refused connection.
function stubServer(status: number | null) {
  const server = {
    status,
    check: vi.fn(async () => {
      if (server.status === null) {
        throw new TypeError('Failed to fetch');
      }
      return new Response(null, { status: server.status });
    }),
  };
  vi.stubGlobal('fetch', server.check);
  return server;
}

function preloadError(payload: Error): Event {
  return Object.assign(new Event('vite:preloadError', { cancelable: true }), { payload });
}

async function recoverOnFreshPage() {
  const page = await loadPage();
  page.recoverFromModuleError();
  await vi.advanceTimersByTimeAsync(0);
  return page.moduleRecovery();
}

beforeEach(() => {
  vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
});

afterEach(() => {
  vi.useRealTimers();
  sessionStorage.clear();
});

describe('isModuleLoadError', () => {
  it.each([
    chromiumError,
    'error loading dynamically imported module: https://portal.example.com/assets/ModelsPage-BmP2x7Qa.js',
    'Importing a module script failed.',
    'Unable to preload CSS for https://portal.example.com/assets/ModelsPage-Dk3lE0aZ.css',
  ])('recognizes "%s"', async (message) => {
    const { isModuleLoadError } = await loadPage();
    expect(isModuleLoadError(new TypeError(message))).toBe(true);
  });

  it('ignores errors a reload would not fix', async () => {
    const { isModuleLoadError } = await loadPage();
    expect(isModuleLoadError(new ReferenceError('process is not defined'))).toBe(false);
    expect(isModuleLoadError(new TypeError("Cannot read properties of undefined (reading 'ModelsPage')"))).toBe(false);
    expect(isModuleLoadError(chromiumError)).toBe(false);
    expect(isModuleLoadError(undefined)).toBe(false);
  });
});

describe('recoverFromModuleError', () => {
  it('treats an error response, such as a proxy reporting 502, as a server that is still down', async () => {
    const reload = stubReload();
    const server = stubServer(502);
    expect(await recoverOnFreshPage()).toBe('waiting');
    expect(reload).not.toHaveBeenCalled();

    server.status = 200;
    await vi.advanceTimersByTimeAsync(2_000);
    expect(reload).toHaveBeenCalledOnce();
  });

  it('refuses right after a reload and reloads again once the guard window has passed', async () => {
    const reload = stubReload();
    stubServer(200);
    const now = vi.spyOn(Date, 'now').mockReturnValue(1_000_000);
    expect(await recoverOnFreshPage()).toBe('reloading');

    now.mockReturnValue(1_001_000);
    expect(await recoverOnFreshPage()).toBe('failed');

    now.mockReturnValue(1_060_000);
    expect(await recoverOnFreshPage()).toBe('reloading');
    expect(reload).toHaveBeenCalledTimes(2);
  });

  it('does not reload without session storage, since nothing would stop a loop', async () => {
    const reload = stubReload();
    stubServer(200);
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new DOMException('The operation is insecure.', 'SecurityError');
    });

    expect(await recoverOnFreshPage()).toBe('failed');
    expect(reload).not.toHaveBeenCalled();
  });
});

describe('recoverOnPreloadError', () => {
  it('waits for the server when Vite fails to load a dynamic import, then reloads once', async () => {
    const reload = stubReload();
    const server = stubServer(null);
    const page = await loadPage();
    onTestFinished(page.recoverOnPreloadError());

    window.dispatchEvent(preloadError(new ReferenceError('process is not defined')));
    await vi.advanceTimersByTimeAsync(0);
    expect(server.check).not.toHaveBeenCalled();

    const first = preloadError(new TypeError(chromiumError));
    window.dispatchEvent(first);
    window.dispatchEvent(preloadError(new TypeError(chromiumError)));
    await vi.advanceTimersByTimeAsync(4_000);
    expect(server.check).toHaveBeenCalledTimes(3);
    expect(page.moduleRecovery()).toBe('waiting');
    expect(reload).not.toHaveBeenCalled();

    server.status = 200;
    await vi.advanceTimersByTimeAsync(2_000);
    expect(reload).toHaveBeenCalledOnce();
    expect(first.defaultPrevented).toBe(false);
  });
});
