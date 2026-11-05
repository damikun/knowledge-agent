import { Plus, Trash2 } from 'lucide-react';
import { useMemo } from 'react';
import { Button } from '../../../components/ui/Button';
import { fieldAria } from '../../../components/ui/aria';
import { Field, inputProps } from '../../../components/ui/Field';
import { Select } from '../../../components/ui/Select';
import { WEEKDAYS } from '../api';
import type { Weekday } from '../types';
import { hoursRow, type WidgetFormValues } from '../widgetForm';
import { WEEKDAY_LABELS } from '../widgetTexts';
import { ControlLabel, describedBy, FieldMessage, type ControlProps } from './controlParts';

export function TimeZoneControl({ property, kit, controlId, errors, action }: ControlProps<'timeZone'>) {
  const zones = useMemo(() => (typeof Intl.supportedValuesOf === 'function' ? Intl.supportedValuesOf('timeZone') : []), []);
  const listId = `${controlId}-zones`;
  return (
    <Field label={property.label} htmlFor={controlId} errors={errors} hint={property.hint} action={action}>
      <input
        {...fieldAria(controlId, errors, Boolean(property.hint))}
        {...inputProps}
        list={listId}
        value={kit.values.timeZone}
        onChange={(event) => kit.set('timeZone', event.target.value)}
        className="field"
      />
      <datalist id={listId}>
        {zones.map((zone) => (
          <option key={zone} value={zone} />
        ))}
      </datalist>
    </Field>
  );
}

function nextDay(rows: WidgetFormValues['hours']): Weekday {
  const last = rows.at(-1)?.day;
  return last ? (WEEKDAYS[(WEEKDAYS.indexOf(last) + 1) % WEEKDAYS.length] ?? 'monday') : 'monday';
}

export function HoursControl({ property, kit, controlId, errors, action }: ControlProps<'hours'>) {
  const rows = kit.values.hours;
  const update = (next: WidgetFormValues['hours']) => kit.set('hours', next);
  const change = (key: string, patch: Partial<WidgetFormValues['hours'][number]>) =>
    update(rows.map((row) => (row.key === key ? { ...row, ...patch } : row)));
  const hint =
    rows.length === 0
      ? 'Add the hours when someone answers. Without them, the team always counts as away.'
      : 'Outside these hours, visitors hear that the team is away, and Slack requests go by email.';
  return (
    <fieldset aria-describedby={describedBy(controlId, errors, hint)}>
      <ControlLabel as="legend" action={action}>
        {property.label}
      </ControlLabel>
      <div className="mt-1.5 space-y-1.5">
        {rows.map((row, index) => (
          <div key={row.key} className="flex flex-wrap items-center gap-1.5">
            <Select
              id={index === 0 ? controlId : undefined}
              aria-label={`Day of row ${index + 1}`}
              value={row.day}
              onChange={(day) => change(row.key, { day })}
              options={WEEKDAYS.map((day) => ({ value: day, label: WEEKDAY_LABELS[day] }))}
              className="w-32"
            />
            <input
              type="time"
              aria-label={`Opens, row ${index + 1}`}
              aria-invalid={errors ? true : undefined}
              value={row.opens}
              onChange={(event) => change(row.key, { opens: event.target.value })}
              className="field w-28"
            />
            <span className="text-sm text-fg-muted">to</span>
            <input
              type="time"
              aria-label={`Closes, row ${index + 1}`}
              aria-invalid={errors ? true : undefined}
              value={row.closes}
              onChange={(event) => change(row.key, { closes: event.target.value })}
              className="field w-28"
            />
            <Button variant="ghost" size="icon" icon={Trash2} aria-label={`Remove row ${index + 1}`} onClick={() => update(rows.filter((item) => item.key !== row.key))} />
          </div>
        ))}
        <Button size="sm" icon={Plus} id={rows.length === 0 ? controlId : undefined} onClick={() => update([...rows, hoursRow(nextDay(rows))])}>
          Add hours
        </Button>
      </div>
      <FieldMessage id={controlId} errors={errors} hint={hint} />
    </fieldset>
  );
}
