import { BadgeCheck, Search, X } from 'lucide-react';
import { useId, useState } from 'react';
import { Button } from '../../components/ui/Button';
import { Spinner } from '../../components/ui/feedback';
import { useDebouncedValue } from '../../lib/hooks/useDebouncedValue';
import { usePeople, usePerson } from './api';

/** Falls back to the id's start while loading or when the person is gone. */
export function usePersonName(personId: string): string {
  const person = usePerson(personId);
  if (!personId) {
    return '';
  }
  return person.data?.name ?? (person.isError ? 'Erased contact' : 'Loading…');
}

export interface PersonFilterProps {
  /** Empty for anyone. */
  value: string;
  onChange: (personId: string) => void;
}

export function PersonFilter({ value, onChange }: PersonFilterProps) {
  const id = useId();
  const [input, setInput] = useState('');
  const query = useDebouncedValue(input.trim(), 300);
  const name = usePersonName(value);
  const people = usePeople({ q: query, widgetId: '', identified: '' }, query !== '' && !value);
  const matches = query ? (people.data?.pages[0]?.items ?? []).slice(0, 5) : [];

  if (value) {
    return (
      <div className="flex items-center gap-2 rounded-md border border-border-strong bg-surface px-2.5 py-1 text-small">
        <span className="text-fg-muted">Contact</span>
        <span className="min-w-0 flex-1 truncate font-medium text-fg">{name}</span>
        <Button size="icon-sm" variant="ghost" icon={X} aria-label={`Remove filter: Contact: ${name}`} onClick={() => onChange('')} />
      </div>
    );
  }

  return (
    <div className="space-y-1">
      <div className="relative">
        <label htmlFor={`${id}-q`} className="sr-only">
          Contact
        </label>
        <Search className="pointer-events-none absolute top-1/2 left-2.5 size-4 -translate-y-1/2 text-fg-muted" aria-hidden />
        <input
          id={`${id}-q`}
          type="search"
          value={input}
          onChange={(event) => setInput(event.target.value)}
          placeholder="Contact: name or email"
          autoComplete="off"
          aria-describedby={`${id}-results`}
          className="field pl-8"
        />
      </div>
      <div id={`${id}-results`} aria-live="polite">
        {query && people.isFetching && matches.length === 0 ? (
          <Spinner label="Looking for people" className="px-1 py-1" />
        ) : query && matches.length === 0 ? (
          <p className="px-1 text-xs text-fg-muted">Nobody matches.</p>
        ) : matches.length > 0 ? (
          <ul aria-label="Matching people" className="space-y-0.5">
            {matches.map((person) => (
              <li key={person.id}>
                <button
                  type="button"
                  onClick={() => {
                    setInput('');
                    onChange(person.id);
                  }}
                  className="flex w-full min-w-0 items-center gap-2 rounded-md px-2 py-1.5 text-left text-small hover:bg-fill-hover"
                >
                  <span className="truncate font-medium text-fg">{person.name}</span>
                  {person.identified ? <BadgeCheck className="size-3.5 shrink-0 text-accent-text" aria-label="Signed in" /> : null}
                  <span className="ml-auto truncate text-xs text-fg-muted">{person.email ?? ''}</span>
                </button>
              </li>
            ))}
          </ul>
        ) : null}
      </div>
    </div>
  );
}
