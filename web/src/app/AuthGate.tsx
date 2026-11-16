import { useQueryClient } from '@tanstack/react-query';
import { useEffect, useState, type ReactNode } from 'react';
import { authKeys, clearSignInMarks, hasNoRole, useMe } from '../features/auth/api';
import { AuthErrorScreen, NoRoleScreen, SignInGate } from '../features/auth/SignInScreen';
import { onForbidden, onUnauthorized } from '../lib/session';
import { AppLoading } from './RouteErrorPage';

export function AuthGate({ children }: { children: ReactNode }) {
  const queryClient = useQueryClient();
  const me = useMe();

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
