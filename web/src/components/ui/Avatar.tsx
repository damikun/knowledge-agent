import { clsx } from 'clsx';
import type { LucideIcon } from 'lucide-react';
import { TONES, type Tone } from './tone';

type AvatarSize = 'sm' | 'md' | 'lg';

// 28, 32 and 40 px: the header, list rows, and cards or someone's details.
const sizes: Record<AvatarSize, { box: string; icon: string }> = {
  sm: { box: 'size-7 text-xs', icon: 'size-3.5' },
  md: { box: 'size-8 text-xs', icon: 'size-4' },
  lg: { box: 'size-10 text-sm', icon: 'size-5' },
};

/** "Ada Lovelace" gives "AL", "Shop assistant" gives "SA"; an e-mail address uses its local part. */
export function initials(name: string): string {
  const words = (name.split('@')[0] ?? '').split(/\s+/).filter(Boolean);
  const picked = words.length > 1 ? [words[0]!, words.at(-1)!] : words;
  return (
    picked
      .map((word) => Array.from(word)[0] ?? '')
      .join('')
      .toUpperCase() || '?'
  );
}

export interface AvatarProps {
  name?: string;
  src?: string;
  /** Replaces the initials. */
  icon?: LucideIcon;
  size?: AvatarSize;
  /** People are round; things such as agents and providers are tiles. */
  shape?: 'circle' | 'tile';
  tone?: Tone;
  className?: string;
}

/** Decorative: the name next to it says who or what it is. */
export function Avatar({ name = '', src, icon: Icon, size = 'md', shape = 'circle', tone = 'neutral', className }: AvatarProps) {
  return (
    <span
      aria-hidden
      className={clsx(
        'flex shrink-0 items-center justify-center overflow-hidden font-medium select-none',
        sizes[size].box,
        shape === 'circle' ? 'rounded-full' : 'rounded-lg',
        TONES[tone].subtle,
        TONES[tone].text,
        className,
      )}
    >
      {src ? <img src={src} alt="" className="size-full object-cover" /> : Icon ? <Icon className={sizes[size].icon} /> : initials(name)}
    </span>
  );
}
