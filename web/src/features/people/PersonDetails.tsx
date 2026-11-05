import { useEffect, useId, useRef, useState, type ReactNode } from 'react';
import { Badge, type BadgeTone } from '../../components/ui/Badge';
import { Card, ErrorMessage } from '../../components/ui/feedback';
import { formatDateTime, formatNumber } from '../../lib/format';
import { useSetPersonAttributes, useUpdatePerson } from './api';
import { EditableRow, type EditableKind } from './EditableRow';
import { formatAttributeValue, SOURCE_HINTS, SOURCE_LABELS } from './filters';
import type { AttributeSource, AttributeValue, Person, PersonAttribute } from './types';

const SOURCE_TONES: Record<AttributeSource, BadgeTone> = {
  verified: 'accent',
  server: 'success',
  admin: 'neutral',
  browser: 'warning',
};

function SourceBadge({ source, updatedAt }: { source: AttributeSource; updatedAt?: string }) {
  return (
    <Badge tone={SOURCE_TONES[source]} title={`${SOURCE_HINTS[source]}${updatedAt ? `. Last changed ${formatDateTime(updatedAt)}` : ''}`}>
      {SOURCE_LABELS[source]}
    </Badge>
  );
}

const NOTES_DELAY_MS = 800;

const IDENTIFIED_WARNING = 'Your website sends this for signed-in users, so their next sign-in may replace your edit.';

// Autosaves when typing pauses and on blur.
function NotesField({ person }: { person: Person }) {
  const id = useId();
  const update = useUpdatePerson(person.id);
  const stored = person.notes ?? '';
  const [notes, setNotes] = useState(stored);
  const [seen, setSeen] = useState(stored);
  const [status, setStatus] = useState<'idle' | 'saving' | 'saved' | 'failed'>('idle');
  const timer = useRef<number | null>(null);

  // Follow outside changes while there are no local edits; after our own save, take the server-trimmed text.
  if (stored !== seen) {
    setSeen(stored);
    if (notes.trim() === seen.trim() || notes.trim() === stored.trim()) {
      setNotes(stored);
    }
  }

  useEffect(() => () => {
    if (timer.current !== null) {
      window.clearTimeout(timer.current);
    }
  }, []);

  const save = (value: string) => {
    if (timer.current !== null) {
      window.clearTimeout(timer.current);
      timer.current = null;
    }
    if (value.trim() === stored.trim()) {
      return;
    }
    setStatus('saving');
    update.mutate({ notes: value.trim() }, { onSuccess: () => setStatus('saved'), onError: () => setStatus('failed') });
  };

  return (
    <div className="space-y-1.5 border-t border-border-subtle pt-3">
      <div className="flex items-center justify-between gap-2">
        <label htmlFor={`${id}-notes`} className="text-small font-medium text-fg">
          Notes
        </label>
        <span role="status" className="text-xs text-fg-muted">
          {status === 'saving' ? 'Saving' : status === 'saved' ? 'Saved' : status === 'failed' ? 'Not saved' : ''}
        </span>
      </div>
      <textarea
        id={`${id}-notes`}
        className="field min-h-24 py-2"
        rows={4}
        maxLength={4000}
        value={notes}
        placeholder="Only staff see them."
        onChange={(event) => {
          const value = event.target.value;
          setNotes(value);
          setStatus('idle');
          if (timer.current !== null) {
            window.clearTimeout(timer.current);
          }
          timer.current = window.setTimeout(() => save(value), NOTES_DELAY_MS);
        }}
        onBlur={() => save(notes)}
      />
      {status === 'failed' && update.error ? <ErrorMessage error={update.error} title="The notes could not be saved" /> : null}
    </div>
  );
}

export function DetailsCard({ person, admin }: { person: Person; admin: boolean }) {
  const update = useUpdatePerson(person.id);
  // The website owns name and email of signed-in users; only admins can correct them.
  const canEdit = !person.identified || admin;
  const saveField = (field: 'name' | 'email') => (value: string) => update.mutateAsync({ [field]: value });
  return (
    <Card>
      <section aria-labelledby="person-details" className="space-y-2 p-4">
        <h2 id="person-details" className="text-section text-fg">
          Details
        </h2>
        {person.identified ? <p className="text-xs text-fg-muted">Your website sends the name and email of this signed-in user.</p> : null}
        <div>
          <EditableRow
            label="Name"
            value={person.savedName ?? ''}
            maxLength={100}
            badge={person.identified && person.savedName ? <SourceBadge source="verified" /> : null}
            warning={person.identified ? IDENTIFIED_WARNING : undefined}
            {...(canEdit ? { onSave: saveField('name') } : {})}
          />
          <EditableRow
            label="Email"
            kind="email"
            value={person.email ?? ''}
            maxLength={254}
            badge={person.identified && person.email ? <SourceBadge source="verified" /> : null}
            warning={person.identified ? IDENTIFIED_WARNING : undefined}
            {...(canEdit ? { onSave: saveField('email') } : {})}
          />
        </div>
        <NotesField person={person} />
      </section>
    </Card>
  );
}

function editKind(type: string): EditableKind {
  return type === 'number' ? 'number' : type === 'boolean' ? 'boolean' : type === 'date' ? 'date' : 'text';
}

function editValue(attribute: PersonAttribute): string {
  if (attribute.type === 'date' && typeof attribute.value === 'string') {
    return attribute.value.slice(0, 10);
  }
  return String(attribute.value);
}

function typedValue(type: string, text: string): AttributeValue | null {
  if (text === '') {
    return null;
  }
  if (type === 'number') {
    const number = Number(text);
    return Number.isFinite(number) ? number : text;
  }
  return type === 'boolean' ? text === 'true' : text;
}

export function AboutCard({ person, admin }: { person: Person; admin: boolean }) {
  const setAttributes = useSetPersonAttributes(person.id);
  // Hidden attributes stay stored and filterable; admins still see them.
  const attributes = person.attributes.filter((attribute) => attribute.visible || admin);
  const row = (label: string, value: ReactNode) => <EditableRow label={label} value="" display={value} />;
  return (
    <Card>
      <section aria-labelledby="person-about" className="space-y-3 p-4">
        <h2 id="person-about" className="text-section text-fg">
          About
        </h2>
        <div>
          {person.externalId ? row('User id', <span className="font-mono text-xs" title={person.externalId}>{person.externalId}</span>) : null}
          {row('First seen', formatDateTime(person.firstSeenAt))}
          {row('Last seen', formatDateTime(person.lastSeenAt))}
          {row('Sessions', `${formatNumber(person.sessionCount)}${person.returning ? ', returning' : ''}`)}
          {person.lastPageUrl ? row('Last page', <span className="break-all">{person.lastPageUrl}</span>) : null}
          {person.lastReferrer ? row('Referrer', <span className="break-all">{person.lastReferrer}</span>) : null}
          {person.language ? row('Language', person.language) : null}
          {row('Widgets', person.widgets.length > 0 ? person.widgets.map((widget) => widget.name).join(', ') : 'None')}
        </div>
        {attributes.length > 0 ? (
          <div className="space-y-1 border-t border-border-subtle pt-3">
            <h3 className="text-small font-medium text-fg">Attributes</h3>
            <p className="text-xs text-fg-muted">What your website says about this person.{admin ? ' Your edits are saved as Admin and win over the website.' : ''}</p>
            {attributes.map((attribute) => (
              <EditableRow
                key={attribute.key}
                label={attribute.visible ? attribute.label : `${attribute.label} (hidden)`}
                kind={editKind(attribute.type)}
                value={editValue(attribute)}
                display={formatAttributeValue(attribute.value, attribute.type)}
                badge={<SourceBadge source={attribute.source} updatedAt={attribute.updatedAt} />}
                maxLength={255}
                {...(admin ? { onSave: (text: string) => setAttributes.mutateAsync({ [attribute.key]: typedValue(attribute.type, text) }) } : {})}
              />
            ))}
          </div>
        ) : null}
      </section>
    </Card>
  );
}
