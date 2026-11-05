import { useContext, useEffect, useState } from 'react';
import { UNSAFE_DataRouterContext, useBlocker, type Location } from 'react-router';
import { ConfirmDialog } from './Modal';

/** Made once per page by useLeaveGuard; the page calls allowLeave right before a navigation of its own. */
export interface LeaveGuardHandle {
  allowLeave: () => void;
  isLeaveAllowed: () => boolean;
}

/** allowLeave has to apply in the same event as the navigation, before React re-renders, so the flag lives outside state. */
export function useLeaveGuard(): LeaveGuardHandle {
  const [handle] = useState<LeaveGuardHandle>(() => {
    let allowed = false;
    return {
      allowLeave: () => {
        allowed = true;
      },
      isLeaveAllowed: () => allowed,
    };
  });
  return handle;
}

export interface LeaveGuardProps {
  when: boolean;
  guard: LeaveGuardHandle;
  /** Navigations that stay on the same form aren't asked about. Default: same path and search. */
  samePlace?: (current: Location, next: Location) => boolean;
  message?: string;
}

const samePathAndSearch = (current: Location, next: Location) => current.pathname + current.search === next.pathname + next.search;

function Blocker({ when, guard, samePlace = samePathAndSearch, message }: LeaveGuardProps) {
  const blocker = useBlocker(({ currentLocation, nextLocation }) => when && !guard.isLeaveAllowed() && !samePlace(currentLocation, nextLocation));
  return (
    <ConfirmDialog
      open={blocker.state === 'blocked'}
      title="Leave without saving?"
      message={message ?? 'Your changes to this agent are not saved yet. If you leave now, they are lost.'}
      confirmLabel="Leave"
      onConfirm={() => blocker.proceed?.()}
      onCancel={() => blocker.reset?.()}
    />
  );
}

export function LeaveGuard(props: LeaveGuardProps) {
  const { when, guard } = props;
  // useBlocker needs a data router; the app has one, but tests that mount a page in a MemoryRouter do not.
  const dataRouter = useContext(UNSAFE_DataRouterContext);

  useEffect(() => {
    if (!when) {
      return;
    }
    const warn = (event: BeforeUnloadEvent) => {
      if (!guard.isLeaveAllowed()) {
        event.preventDefault();
      }
    };
    window.addEventListener('beforeunload', warn);
    return () => window.removeEventListener('beforeunload', warn);
  }, [when, guard]);

  return dataRouter ? <Blocker {...props} /> : null;
}
