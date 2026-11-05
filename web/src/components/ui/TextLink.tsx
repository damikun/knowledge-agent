import { clsx } from 'clsx';
import { Link, type LinkProps } from 'react-router';

export function TextLink({ className, ...props }: LinkProps) {
  return <Link className={clsx('font-medium text-accent-text underline-offset-2 hover:underline', className)} {...props} />;
}
