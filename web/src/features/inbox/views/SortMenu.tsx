import { clsx } from 'clsx';
import { ArrowUpDown, Check } from 'lucide-react';
import { Button } from '../../../components/ui/Button';
import { Menu } from '../../../components/ui/Menu';
import type { ConversationSort } from '../types';
import { textOf, type Choices } from './FilterBar';

const SORTS: Choices<ConversationSort> = [
  ['updated_desc', 'Latest activity'],
  ['updated_asc', 'Oldest activity'],
  ['created_desc', 'Newest first'],
  ['created_asc', 'Oldest first'],
];

export function SortMenu({ value, onChange }: { value: ConversationSort; onChange: (sort: ConversationSort) => void }) {
  const current = textOf(SORTS, value);
  return (
    <Menu
      label="Sort"
      align="end"
      items={SORTS.map(([sort, text]) => ({
        label: (
          <span className={clsx('flex w-full items-center gap-2', sort === value && 'font-medium')}>
            <span className="flex-1">{text}</span>
            {sort === value ? (
              <>
                <Check className="size-4 text-accent-icon" aria-hidden />
                <span className="sr-only"> (current)</span>
              </>
            ) : null}
          </span>
        ),
        onSelect: () => onChange(sort),
      }))}
      trigger={(props) => <Button {...props} variant="ghost" size="icon" icon={ArrowUpDown} aria-label={`Sort: ${current}`} title={`Sort: ${current}`} />}
    />
  );
}
