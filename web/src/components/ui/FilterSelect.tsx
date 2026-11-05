import { clsx } from 'clsx';
import { Select } from './Select';

export interface FilterSelectProps<T extends string> {
  label: string;
  value: T;
  /** The first choice names the filter, such as "All widgets", so the label is for screen readers only. */
  choices: ReadonlyArray<readonly [T, string]>;
  onChange: (value: T) => void;
  wide?: boolean;
  className?: string;
}

export function FilterSelect<T extends string>({ label, value, choices, onChange, wide = false, className }: FilterSelectProps<T>) {
  return (
    <div className={clsx('w-full min-w-0', wide ? 'sm:w-56' : 'sm:w-44', className)}>
      <Select aria-label={label} value={value} options={choices.map(([choice, text]) => ({ value: choice, label: text }))} onChange={onChange} />
    </div>
  );
}
