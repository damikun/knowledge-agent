import { clsx } from 'clsx';
import { Menu, X } from 'lucide-react';
import { useId, useState } from 'react';
import { Outlet, useLocation } from 'react-router';
import { Button } from '../components/ui/Button';
import { Drawer } from '../components/ui/Drawer';
import { Toaster } from '../components/ui/Toast';
import { isAdmin, useMe } from '../features/auth/api';
import { UserMenu } from '../features/auth/UserMenu';
import { AlertsBell } from '../features/alerts';
import { HelpLauncher } from '../features/help';
import { useWaitingCount } from '../features/inbox/api';
import { SystemBanners } from '../features/system/SystemBanners';
import { SystemStatus } from '../features/system/SystemStatus';
import { BreadcrumbContext } from '../lib/breadcrumb';
import { CurrentPlaceContext, readReturnTo } from '../lib/returnTo';
import { useSidebar } from '../lib/sidebar';
import { Breadcrumb } from './Breadcrumb';
import { Brand, findSection, MenuToggle, Navigation, PRODUCT, VersionLabel } from './Navigation';

export function AppLayout() {
  const [navOpen, setNavOpen] = useState(false);
  const [item, setItem] = useState<string | null>(null);
  const sidebar = useSidebar();
  const sidebarId = useId();
  const location = useLocation();
  const admin = isAdmin(useMe().data);
  const waiting = useWaitingCount();
  const section = findSection(location.pathname);
  const returnTo = readReturnTo(location.state);
  const place = item ?? section?.label ?? PRODUCT;

  return (
    <BreadcrumbContext value={setItem}>
      <CurrentPlaceContext value={place}>
        {/* React 19 hoists <title> into the head. */}
        <title>{[item, section?.label, PRODUCT].filter(Boolean).join(' · ')}</title>
        <div className="relative flex h-dvh overflow-hidden bg-sidebar">
          <a
            href="#main"
            className="sr-only focus:not-sr-only focus:fixed focus:top-3 focus:left-3 focus:z-50 focus:rounded-md focus:bg-accent focus:px-3 focus:py-1.5 focus:text-sm focus:font-medium focus:text-fg-on-accent focus:shadow-menu"
          >
            Skip to content
          </a>

          <aside id={sidebarId} className={clsx('hidden shrink-0 flex-col py-2 md:flex', sidebar.collapsed ? 'w-14' : 'w-60')}>
            <Brand compact={sidebar.collapsed} />
            <Navigation admin={admin} waiting={waiting} compact={sidebar.collapsed} />
            <MenuToggle admin={admin} collapsed={sidebar.collapsed} onToggle={sidebar.toggle} controls={sidebarId} />
          </aside>

          <Drawer open={navOpen} onClose={() => setNavOpen(false)} label="Navigation">
            <div className="flex items-center justify-between pr-2">
              <Brand />
              <Button variant="ghost" size="icon" icon={X} aria-label="Close navigation" onClick={() => setNavOpen(false)} />
            </div>
            <Navigation admin={admin} waiting={waiting} onNavigate={() => setNavOpen(false)} />
            {admin ? (
              <div className="flex h-12 shrink-0 items-center px-3">
                <VersionLabel />
              </div>
            ) : null}
          </Drawer>

          <div className="flex min-w-0 flex-1 flex-col overflow-hidden bg-canvas md:my-2 md:mr-2 md:rounded-2xl md:border md:border-border md:shadow-panel">
            {admin ? <SystemBanners /> : null}
            <header className="flex h-12 shrink-0 items-center gap-2 border-b border-border-subtle px-4 lg:px-6">
              <Button
                variant="ghost"
                size="icon"
                icon={Menu}
                className="-ml-2 md:hidden"
                aria-label="Open navigation"
                aria-expanded={navOpen}
                onClick={() => setNavOpen(true)}
              />
              <Breadcrumb section={section} item={item} returnTo={returnTo} />
              <div className="flex items-center gap-1">
                {admin ? <SystemStatus /> : null}
                <HelpLauncher />
                <AlertsBell />
                <UserMenu />
              </div>
            </header>
            <main id="main" className="min-h-0 flex-1 overflow-hidden" tabIndex={-1}>
              <Outlet />
            </main>
          </div>
          <Toaster />
        </div>
      </CurrentPlaceContext>
    </BreadcrumbContext>
  );
}
