import { useQueryClient } from '@tanstack/react-query';
import { useEffect, useState, type ReactNode } from 'react';
import { authKeys, clearSignInMarks, hasNoRole, useMe } from '../features/auth/api';
import { AuthErrorScreen, NoRoleScreen, SignInGate } from '../features/auth/SignInScreen';
import { onForbidden, onUnauthorized } from '../lib/session';
import { AppLoading } from './RouteErrorPage';

export function AuthGate({ children }: { children: ReactNode }) {
  const queryClient = useQueryClient();
  const me = useMe();

  // A lost session shows the sign-in screen instead of redirecting, so nothing typed is lost.
  const signedIn = Boolean(me.data);
  const [wasSignedIn, setWasSignedIn] = useState(false);
  if (signedIn && !wasSignedIn) {
    setWasSignedIn(true);
  }
  useEffect(() => {
    if (signedIn) {
      clearSignInMarks();
    }
  }, [signedIn]);

  // 401: the session ended. 403: the last check may have dropped a role. Refetch /me in both cases; an
  // in-flight /me fetch is reused, so a 401 from /me itself can't start another round.
  useEffect(() => {
    const askAgain = () => void queryClient.invalidateQueries({ queryKey: authKeys.me }, { cancelRefetch: false });
    const stopUnauthorized = onUnauthorized(askAgain);
    const stopForbidden = onForbidden(askAgain);
    return () => {
      stopUnauthorized();
      stopForbidden();
    };
  }, [queryClient]);

  if (me.isPending) {
    return <AppLoading />;
  }
  if (me.data === null) {
    return <SignInGate sessionEnded={wasSignedIn} />;
  }
  if (!me.data) {
    return <AuthErrorScreen error={me.error} onRetry={() => void me.refetch()} />;
  }
  if (hasNoRole(me.data)) {
    return <NoRoleScreen user={me.data} />;
  }
  return children;
}
