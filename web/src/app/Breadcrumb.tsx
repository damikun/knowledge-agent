import { Link } from 'react-router';
import { ReturnLink } from '../components/ui/ReturnLink';
import type { ReturnTo } from '../lib/returnTo';
import { PRODUCT, type NavItem } from './Navigation';

export function Breadcrumb({ section, item, returnTo }: { section: NavItem | undefined; item: string | null; returnTo: ReturnTo | null }) {
  if (!returnTo && (!section || !item)) {
    return <div className="min-w-0 flex-1 truncate text-sm font-medium text-fg">{section?.label ?? PRODUCT}</div>;
  }
  const separator = (
    <span aria-hidden className="text-fg-muted">
      /
    </span>
  );
  const current = (label: string) => (
    <span aria-current="page" className="max-w-[28rem] truncate font-medium text-fg" title={label}>
      {label}
    </span>
  );
  const back = returnTo ?? (section && item ? { label: section.label, to: section.to } : null);
  return (
    <nav aria-label="Location" className="flex min-w-0 flex-1 items-center gap-1">
      {back ? <ReturnLink returnTo={back} className="-ml-2" /> : null}
      <ol className="flex min-w-0 items-center gap-1.5 text-sm">
        {returnTo ? (
          <li className="min-w-0 max-w-60 shrink truncate">
            <ReturnLink returnTo={returnTo}>{returnTo.label}</ReturnLink>
          </li>
        ) : null}
        {section && item ? (
          <>
            <li className="flex shrink-0 items-center gap-1.5">
              {returnTo ? separator : null}
              <Link to={section.to} className="rounded-sm text-fg-secondary hover:text-fg">
                {section.label}
              </Link>
            </li>
            <li className="flex min-w-0 items-center gap-1.5">
              {separator}
              {current(item)}
            </li>
          </>
        ) : (
          // The section name is short, so the origin truncates first.
          <li className="flex shrink-0 items-center gap-1.5">
            {separator}
            {current(item ?? section?.label ?? PRODUCT)}
          </li>
        )}
      </ol>
    </nav>
  );
}
