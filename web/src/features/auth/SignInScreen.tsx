import { LogIn, LogOut, RotateCw } from 'lucide-react';
import { useEffect, type ReactNode } from 'react';
import { useLocation } from 'react-router';
import { Button } from '../../components/ui/Button';
import { Alert, Spinner } from '../../components/ui/feedback';
import { errorMessage } from '../../lib/api';
import { AUTH_ERROR_PARAM, redirectedRecently, returnUrlOf, signIn, signOut, wasSignedOut } from './api';
import type { CurrentUserDto } from './types';

// The reason comes from the URL, so a crafted link cannot fill the screen with text.
const MAX_REASON_LENGTH = 300;

function AuthCard({ pageTitle, children }: { pageTitle: string; children: ReactNode }) {
  useEffect(() => {
    document.title = `${pageTitle} · Knowledge Agent`;
  }, [pageTitle]);

  return (
    <main className="flex min-h-dvh items-center justify-center bg-sidebar px-4 py-10">
      <div className="w-full max-w-sm rounded-2xl border border-border bg-surface p-8">
        <span aria-hidden className="flex size-9 items-center justify-center rounded-lg bg-fg text-xs font-semibold tracking-tight text-canvas">
          KA
        </span>
        <h1 className="mt-5 text-title text-fg">Knowledge Agent</h1>
        {children}
      </div>
    </main>
  );
}

function Lead({ children }: { children: ReactNode }) {
  return <p className="mt-1.5 text-sm text-fg-secondary">{children}</p>;
}

export type SignedOutReason = 'expired' | 'signed-out' | 'stalled';

function signedOutReason(sessionEnded: boolean, search: string): SignedOutReason | 'failed' | null {
  if (new URLSearchParams(search).has(AUTH_ERROR_PARAM)) {
    return 'failed';
  }
  if (wasSignedOut()) {
    return 'signed-out';
  }
  // Redirecting would throw away what was being typed.
  if (sessionEnded) {
    return 'expired';
  }
  return redirectedRecently() ? 'stalled' : null;
}

export function SignInGate({ sessionEnded }: { sessionEnded: boolean }) {
  const location = useLocation();
  const reason = signedOutReason(sessionEnded, location.search);
  const returnUrl = returnUrlOf(location);

  useEffect(() => {
    if (reason === null) {
      signIn(returnUrl, true);
    }
  }, [reason, returnUrl]);

  return reason === null ? (
    <div className="flex h-dvh items-center justify-center bg-canvas">
      <Spinner label="Redirecting to sign-in" />
    </div>
  ) : (
    <SignInScreen reason={reason} />
  );
}

const NOTICES: Record<SignedOutReason, { tone: 'info' | 'warning'; text: string }> = {
  'signed-out': { tone: 'info', text: 'You are signed out.' },
  expired: { tone: 'warning', text: 'Your session ended. Sign in again to continue.' },
  stalled: { tone: 'warning', text: 'Sign-in did not complete. Try again, or check that Keycloak is running.' },
};

function SignInScreen({ reason }: { reason?: SignedOutReason | 'failed' }) {
  const location = useLocation();
  const failure = new URLSearchParams(location.search).get(AUTH_ERROR_PARAM)?.trim();
  const notice = reason && reason !== 'failed' ? NOTICES[reason] : null;

  return (
    <AuthCard pageTitle="Sign in">
      <Lead>Sign in to ask questions over your sources and get answers with citations.</Lead>
      {failure ? (
        <Alert tone="danger" className="mt-5" live>
          Sign-in failed: {failure.length > MAX_REASON_LENGTH ? `${failure.slice(0, MAX_REASON_LENGTH)}…` : failure}
        </Alert>
      ) : notice ? (
        <Alert tone={notice.tone} className="mt-5" live>
          {notice.text}
        </Alert>
      ) : null}
      <Button variant="primary" icon={LogIn} className="mt-6 w-full" onClick={() => signIn(returnUrlOf(location))}>
        Sign in
      </Button>
    </AuthCard>
  );
}

/** Signed in, but with neither role the portal knows. */
export function NoRoleScreen({ user }: { user: CurrentUserDto }) {
  return (
    <AuthCard pageTitle="No access">
      <Lead>
        You are signed in as {user.email ?? user.name}, but your account has no role in Knowledge Agent. Ask an administrator
        for the admin or support role.
      </Lead>
      {user.mode === 'oidc' ? (
        <Button variant="secondary" icon={LogOut} className="mt-6 w-full" onClick={signOut}>
          Sign out
        </Button>
      ) : null}
    </AuthCard>
  );
}

export function AuthErrorScreen({ error, onRetry }: { error: unknown; onRetry: () => void }) {
  return (
    <AuthCard pageTitle="Cannot connect">
      <Lead>Could not check whether you are signed in.</Lead>
      <Alert tone="danger" className="mt-5" live>
        {errorMessage(error)}
      </Alert>
      <Button variant="primary" icon={RotateCw} className="mt-6 w-full" onClick={onRetry}>
        Try again
      </Button>
    </AuthCard>
  );
}
