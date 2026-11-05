import { Navigate, useLocation } from 'react-router';
import { readInboxState, toInboxSearch } from './inboxState';

/** Maps old Conversations links (?q, filters, ?open) to the same search in the Inbox. */
export function ConversationsRedirect() {
  const params = new URLSearchParams(useLocation().search);
  if (!params.has('status')) {
    params.set('view', 'all');
  }
  return <Navigate to={{ pathname: '/inbox', search: `?${toInboxSearch(readInboxState(params)).toString()}` }} replace />;
}
