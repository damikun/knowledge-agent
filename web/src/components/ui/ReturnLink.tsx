import { clsx } from 'clsx';
import { ArrowLeft } from 'lucide-react';
import type { MouseEvent, ReactNode } from 'react';
import { Link, useNavigate } from 'react-router';
import { historyIndex, returnTitle, type ReturnTo } from '../../lib/returnTo';

export interface ReturnLinkProps {
  returnTo: ReturnTo;
  /** With text, a quiet text link; without, a 32px back arrow. */
  children?: ReactNode;
  className?: string;
}

/** A real link, so a middle click works. A plain click goes back in history when the origin is the previous entry. */
export function ReturnLink({ returnTo, children, className }: ReturnLinkProps) {
  const navigate = useNavigate();
  const label = `Back to ${returnTo.label}`;

  const onClick = (event: MouseEvent<HTMLAnchorElement>) => {
    if (event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) {
      return;
    }
    const current = historyIndex();
    if (returnTo.idx !== undefined && current !== null && current > returnTo.idx) {
      event.preventDefault();
      void navigate(returnTo.idx - current);
    }
  };

  if (children) {
    return (
      <Link to={returnTo.to} onClick={onClick} title={returnTitle(returnTo.label)} className={clsx('rounded-sm text-fg-secondary hover:text-fg hover:underline', className)}>
        {children}
      </Link>
    );
  }
  return (
    <Link
      to={returnTo.to}
      onClick={onClick}
      aria-label={label}
      title={returnTitle(returnTo.label)}
      className={clsx('inline-flex size-8 shrink-0 items-center justify-center rounded-md text-fg-secondary hover:bg-fill-hover hover:text-fg', className)}
    >
      <ArrowLeft className="size-4" aria-hidden />
    </Link>
  );
}
