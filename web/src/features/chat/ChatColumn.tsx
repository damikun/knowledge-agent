import { clsx } from 'clsx';
import type { ReactNode } from 'react';

export function ChatColumn({ className, children }: { className?: string; children: ReactNode }) {
  return <div className={clsx('mx-auto w-full max-w-3xl px-4', className)}>{children}</div>;
}
