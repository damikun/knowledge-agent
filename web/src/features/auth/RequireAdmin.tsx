import { ShieldAlert } from 'lucide-react';
import { Link, Outlet } from 'react-router';
import { buttonClass } from '../../components/ui/Button';
import { Card, EmptyState } from '../../components/ui/feedback';
import { isAdmin, useMe } from './api';

function AdminRoleRequired() {
  return (
    <div className="flex h-full items-center justify-center overflow-y-auto p-6">
      <Card className="w-full max-w-sm">
        <EmptyState
          icon={ShieldAlert}
          title={<h1>You need the admin role to open this page</h1>}
          description="Ask an administrator for the admin role, or go to the Inbox."
          action={
            <Link to="/inbox" className={buttonClass({ variant: 'primary' })}>
              Go to the Inbox
            </Link>
          }
        />
      </Card>
    </div>
  );
}

export function RequireAdmin() {
  const me = useMe();
  if (!me.data) {
    return null;
  }
  return isAdmin(me.data) ? <Outlet /> : <AdminRoleRequired />;
}
