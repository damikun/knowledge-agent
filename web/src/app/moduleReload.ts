const RELOADED_AT_KEY = 'knowledge-agent.module-reloaded-at';
const RELOAD_GUARD_MS = 10_000;
const RETRY_MS = 2_000;
const CHECK_TIMEOUT_MS = 5_000;

const MODULE_LOAD_ERROR =
  /Failed to fetch dynamically imported module|error loading dynamically imported module|Importing a module script failed|Unable to preload CSS/i;

export type ModuleRecovery = 'idle' | 'checking' | 'waiting' | 'reloading' | 'failed';

let recovery: ModuleRecovery = 'idle';
let attempt = 0;
let retryTimer: ReturnType<typeof setTimeout> | undefined;
const listeners = new Set<() => void>();

export function isModuleLoadError(error: unknown): boolean {
  return error instanceof Error && MODULE_LOAD_ERROR.test(error.message);
}

function setRecovery(next: ModuleRecovery): void {
  recovery = next;
  listeners.forEach((listener) => listener());
}

function reloadOnce(): boolean {
  try {
    if (Date.now() - Number(sessionStorage.getItem(RELOADED_AT_KEY)) < RELOAD_GUARD_MS) {
      return false;
    }
    sessionStorage.setItem(RELOADED_AT_KEY, String(Date.now()));
  } catch {
    return false;
  }
  window.location.reload();
  return true;
}

async function serverAnswers(): Promise<boolean> {
  try {
    const response = await fetch(import.meta.env.BASE_URL, {
      method: 'HEAD',
      cache: 'no-store',
      signal: AbortSignal.timeout(CHECK_TIMEOUT_MS),
    });
    return response.ok;
  } catch {
    return false;
  }
}

async function check(current: number): Promise<void> {
  const answers = await serverAnswers();
  if (current !== attempt) {
    return;
  }
  if (answers) {
    setRecovery(reloadOnce() ? 'reloading' : 'failed');
    return;
  }
  setRecovery('waiting');
  retryTimer = setTimeout(() => void check(current), RETRY_MS);
}

export function moduleRecovery(): ModuleRecovery {
  return recovery;
}

export function subscribeToModuleRecovery(listener: () => void): () => void {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

export function recoverFromModuleError(): void {
  if (recovery === 'checking' || recovery === 'waiting' || recovery === 'reloading') {
    return;
  }
  attempt += 1;
  setRecovery('checking');
  void check(attempt);
}

export function stopModuleRecovery(): void {
  if (recovery === 'reloading') {
    return;
  }
  attempt += 1;
  clearTimeout(retryTimer);
  setRecovery('idle');
}

export function recoverOnPreloadError(): () => void {
  const onPreloadError = (event: VitePreloadErrorEvent) => {
    if (isModuleLoadError(event.payload)) {
      recoverFromModuleError();
    }
  };
  window.addEventListener('vite:preloadError', onPreloadError);
  return () => window.removeEventListener('vite:preloadError', onPreloadError);
}
