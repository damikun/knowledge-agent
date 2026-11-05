import { CircleAlert, Compass } from 'lucide-react';
import { useEffect, useSyncExternalStore } from 'react';
import { isRouteErrorResponse, Link, useRouteError } from 'react-router';
import { Button } from '../components/ui/Button';
import { EmptyState, Spinner } from '../components/ui/feedback';
import { isModuleLoadError, moduleRecovery, recoverFromModuleError, stopModuleRecovery, subscribeToModuleRecovery } from './moduleReload';

export function AppLoading() {
  return (
    <div className="flex h-dvh items-center justify-center bg-canvas">
      <Spinner label="Loading Knowledge Agent" />
    </div>
  );
}

export function NotFoundPage() {
  return (
    <EmptyState
      icon={Compass}
      title="Page not found"
      description="The address does not match any page of the portal."
      action={
        <Link to="/" className="text-sm font-medium text-accent-text hover:underline">
          Go to the start page
        </Link>
      }
      className="h-full"
    />
  );
}

/** When a page's chunk fails to load (after a deploy or a dev server restart), reload once the server answers. */
export function RouteErrorPage() {
  const error = useRouteError();
  const moduleError = isModuleLoadError(error);
  const recovery = useSyncExternalStore(subscribeToModuleRecovery, moduleRecovery);

  useEffect(() => {
    if (!moduleError) {
      return undefined;
    }
    recoverFromModuleError();
    return stopModuleRecovery;
  }, [moduleError]);

  if (moduleError && recovery !== 'waiting' && recovery !== 'failed') {
    return <AppLoading />;
  }

  const message = isRouteErrorResponse(error)
    ? `${error.status} ${error.statusText}`
    : error instanceof Error
      ? error.message
      : 'An unexpected error occurred.';
  return (
    <div className="flex h-dvh items-center justify-center bg-canvas p-6">
      <EmptyState
        icon={CircleAlert}
        title="Something went wrong"
        description={
          moduleError && recovery === 'waiting' ? (
            <>
              {message}
              <span role="status" className="mt-2 block">
                The server isn't responding. The page reloads when it's back.
              </span>
            </>
          ) : (
            message
          )
        }
        action={<Button onClick={() => window.location.reload()}>Reload the page</Button>}
      />
    </div>
  );
}
