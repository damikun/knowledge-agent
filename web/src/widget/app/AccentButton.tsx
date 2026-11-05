import type { ComponentProps } from 'react';

export function AccentButton({ type = 'button', ...props }: Omit<ComponentProps<'button'>, 'className'>) {
  return (
    <button
      type={type}
      {...props}
      className="rounded-full bg-accent px-4 py-2 text-sm font-semibold text-on-accent shadow-sm transition-[transform,background-color] duration-200 ease-(--ease-spring) hover:bg-accent-hover active:scale-95 disabled:opacity-60"
    />
  );
}
