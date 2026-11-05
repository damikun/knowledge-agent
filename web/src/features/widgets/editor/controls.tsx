import { clsx } from 'clsx';
import { ChevronDown, Plus, RotateCcw, Trash2 } from 'lucide-react';
import { useRef, useState } from 'react';
import { fieldAria } from '../../../components/ui/aria';
import { Button } from '../../../components/ui/Button';
import { Alert } from '../../../components/ui/feedback';
import { Field, inputProps } from '../../../components/ui/Field';
import { Popover } from '../../../components/ui/Popover';
import { Select } from '../../../components/ui/Select';
import { Switch } from '../../../components/ui/Switch';
import { TextLink } from '../../../components/ui/TextLink';
import { useReturnState } from '../../../lib/returnTo';
import { ImageUpload } from '../ImageUpload';
import { NUMBER_LIMITS } from '../widgetForm';
import { choiceLabel, LAUNCHER_ICONS } from '../widgetTexts';
import { ColorControl, ColorTokenControl, PresetControl } from './colorControls';
import { ControlLabel, describedBy, FieldMessage, type ControlProps, type EditorKit } from './controlParts';
import { readPath, samePath } from './draft';
import { controlIdOf, type Notice, type PropertyDef } from './registry';
import { HoursControl, TimeZoneControl } from './scheduleControls';

export type { EditorKit } from './controlParts';

function TextControl({ property, kit, controlId, errors, action }: ControlProps<'text'>) {
  const value = readPath(kit.values, property.id);
  return (
    <Field label={property.label} htmlFor={controlId} errors={errors} hint={property.hint} optional={property.optional} action={action}>
      <input
        {...fieldAria(controlId, errors, Boolean(property.hint))}
        {...(property.mono ? inputProps : { autoComplete: 'off' })}
        maxLength={property.maxLength}
        placeholder={property.placeholder}
        value={value}
        onChange={(event) => kit.set(property.id, event.target.value)}
        // Mono text is 12px, so fix the height to match the 32px of other inputs.
        className={clsx('field', property.mono && 'h-8 py-0 font-mono text-xs')}
      />
    </Field>
  );
}

function LongTextControl({ property, kit, controlId, errors, action }: ControlProps<'longText'>) {
  return (
    <Field label={property.label} htmlFor={controlId} errors={errors} hint={property.hint} optional={property.optional} action={action}>
      <textarea
        {...fieldAria(controlId, errors, Boolean(property.hint))}
        {...(property.mono ? inputProps : {})}
        rows={property.rows}
        maxLength={property.maxLength}
        placeholder={property.placeholder}
        value={kit.values[property.id]}
        onChange={(event) => kit.set(property.id, event.target.value)}
        className={clsx('field resize-y', property.mono ? 'font-mono text-xs leading-5' : 'leading-6')}
      />
    </Field>
  );
}

function IconControl({ property, kit, controlId, errors, action }: ControlProps<'icon'>) {
  const [open, setOpen] = useState(false);
  const triggerRef = useRef<HTMLButtonElement>(null);
  const chosenRef = useRef<HTMLButtonElement>(null);
  const current = kit.values.launcherIcon;
  const meta = LAUNCHER_ICONS[current];
  const Current = meta?.icon;
  const labelId = `${controlId}-label`;
  return (
    <div>
      <ControlLabel id={labelId} action={action}>
        {property.label}
      </ControlLabel>
      <button
        ref={triggerRef}
        id={controlId}
        type="button"
        aria-haspopup="dialog"
        aria-expanded={open}
        aria-labelledby={`${labelId} ${controlId}-value`}
        aria-invalid={errors ? true : undefined}
        aria-describedby={describedBy(controlId, errors, false)}
        onClick={() => setOpen((value) => !value)}
        className="field mt-1.5 flex items-center gap-2 text-left"
      >
        {Current ? <Current className="size-4 shrink-0" aria-hidden /> : null}
        <span id={`${controlId}-value`} className="min-w-0 flex-1 truncate">
          {meta?.label ?? current}
        </span>
        <ChevronDown className="size-4 shrink-0 text-fg-muted" aria-hidden />
      </button>
      <FieldMessage id={controlId} errors={errors} />
      <Popover open={open} onClose={() => setOpen(false)} anchorRef={triggerRef} label={property.label} className="p-1" initialFocus={chosenRef}>
        <div role="group" aria-label={property.label} className="grid grid-cols-4 gap-1">
          {property.choices.map((name) => {
            const choice = LAUNCHER_ICONS[name];
            const Icon = choice?.icon;
            const chosen = current === name;
            return (
              <button
                key={name}
                ref={chosen ? chosenRef : undefined}
                type="button"
                aria-pressed={chosen}
                aria-label={choice?.label ?? name}
                title={choice?.label ?? name}
                onClick={() => {
                  kit.set('launcherIcon', name);
                  setOpen(false);
                }}
                className={clsx(
                  'grid size-10 place-items-center rounded-md',
                  chosen ? 'bg-accent-subtle text-accent-text ring-1 ring-accent ring-inset' : 'text-fg-secondary hover:bg-fill-hover hover:text-fg',
                )}
              >
                {Icon ? <Icon className="size-5" aria-hidden /> : <span className="font-mono text-[10px]">{name}</span>}
              </button>
            );
          })}
        </div>
      </Popover>
    </div>
  );
}

function ImageControl({ property, kit, controlId, errors, action }: ControlProps<'image'>) {
  return (
    <div className="relative space-y-1">
      {action ? <div className="absolute top-0 right-0">{action}</div> : null}
      <ImageUpload
        id={controlId}
        label={property.label}
        widgetId={kit.widgetId}
        assetId={kit.values[property.id]}
        onChange={(assetId) => kit.set(property.id, assetId)}
        hint={property.hint ?? ''}
        round={property.round ?? false}
      />
      {errors ? <p className="text-xs text-danger-text">{errors.join(' ')}</p> : null}
    </div>
  );
}

function ToggleControl({ property, kit, controlId, action }: ControlProps<'toggle'>) {
  const hintId = `${controlId}-hint`;
  return (
    <div className="space-y-1">
      <div className="flex items-start justify-between gap-2">
        <Switch
          checked={kit.values[property.id]}
          onChange={(checked) => kit.set(property.id, checked)}
          label={property.label}
          {...(property.hint ? { describedBy: hintId } : {})}
        />
        {action}
      </div>
      {property.hint ? (
        <p id={hintId} className="text-xs text-fg-muted">
          {property.hint}
        </p>
      ) : null}
    </div>
  );
}

function ChoiceControl({ property, kit, controlId, errors, action }: ControlProps<'choice'>) {
  const value = kit.values[property.id];
  const set = (next: string) => kit.set(property.id, next);
  if (property.display === 'select') {
    const all = property.choices.includes(value) ? property.choices : [...property.choices, value];
    return (
      <Field label={property.label} htmlFor={controlId} errors={errors} hint={property.hint} action={action}>
        <Select
          {...fieldAria(controlId, errors, Boolean(property.hint))}
          value={value}
          onChange={set}
          options={all.map((choice) => ({ value: choice, label: choiceLabel(property.labels, choice) }))}
        />
      </Field>
    );
  }
  const focusable = property.choices.includes(value) ? value : property.choices[0];
  return (
    <fieldset aria-describedby={describedBy(controlId, errors, property.hint)}>
      <ControlLabel as="legend" action={action}>
        {property.label}
      </ControlLabel>
      <div className="mt-1.5 flex flex-wrap gap-x-5 gap-y-1">
        {property.choices.map((choice) => (
          <label key={choice} className="inline-flex min-h-8 cursor-pointer items-center gap-2 text-sm text-fg">
            <input type="radio" id={choice === focusable ? controlId : undefined} name={controlId} checked={value === choice} onChange={() => set(choice)} className="size-4 shrink-0" />
            {choiceLabel(property.labels, choice)}
          </label>
        ))}
      </div>
      <FieldMessage id={controlId} errors={errors} hint={property.hint} />
    </fieldset>
  );
}

function ListControl({ property, kit, controlId, errors, action }: ControlProps<'list'>) {
  const items = kit.values.suggestedQuestions;
  const update = (next: string[]) => kit.set('suggestedQuestions', next);
  return (
    <fieldset aria-describedby={describedBy(controlId, errors, property.hint)}>
      <ControlLabel as="legend" action={action}>
        {property.label}
      </ControlLabel>
      <div className="mt-1.5 space-y-1.5">
        {items.map((item, index) => (
          <div key={index} className="flex items-center gap-1.5">
            <input
              id={index === 0 ? controlId : undefined}
              aria-label={`${property.itemLabel} ${index + 1}`}
              aria-invalid={errors ? true : undefined}
              maxLength={property.maxLength}
              autoComplete="off"
              value={item}
              onChange={(event) => update(items.map((current, i) => (i === index ? event.target.value : current)))}
              className="field"
            />
            <Button
              variant="ghost"
              size="icon-sm"
              icon={Trash2}
              aria-label={`Remove ${property.itemLabel.toLowerCase()} ${index + 1}`}
              onClick={() => update(items.filter((_, i) => i !== index))}
            />
          </div>
        ))}
        <Button
          size="sm"
          icon={Plus}
          id={items.length === 0 ? controlId : undefined}
          disabled={items.length >= property.maxItems}
          onClick={() => update([...items, ''])}
        >
          Add {property.itemLabel.toLowerCase()}
        </Button>
      </div>
      <FieldMessage id={controlId} errors={errors} hint={property.hint} />
    </fieldset>
  );
}

function NumberControl({ property, kit, controlId, errors, action }: ControlProps<'number'>) {
  const { min, max } = NUMBER_LIMITS[property.id];
  return (
    <Field label={property.label} htmlFor={controlId} errors={errors} hint={property.hint ?? `From ${min} to ${max}.`} action={action}>
      <input
        {...fieldAria(controlId, errors, true)}
        type="number"
        inputMode="numeric"
        min={min}
        max={max}
        step={1}
        value={kit.values[property.id]}
        onChange={(event) => kit.set(property.id, event.target.value)}
        className="field tabular-nums"
      />
    </Field>
  );
}

function AgentControl({ property, kit, controlId, errors, action }: ControlProps<'agent'>) {
  const returnState = useReturnState();
  const { agents } = kit;
  const value = kit.values.agentId;
  if (agents.length === 0 && !value) {
    return (
      <div className="space-y-1">
        <ControlLabel as="div">{property.label}</ControlLabel>
        <p className="text-sm text-fg-secondary">
          A widget needs an agent of its own, with only the sources visitors may see. Create one on the{' '}
          <TextLink to="/agents" state={returnState}>
            Agents
          </TextLink>{' '}
          page.
        </p>
        {errors ? <p className="text-xs text-danger-text">{errors.join(' ')}</p> : null}
      </div>
    );
  }
  const known = agents.some((agent) => agent.id === value);
  return (
    <Field
      label={property.label}
      htmlFor={controlId}
      errors={errors}
      action={action}
      hint={
        <>
          Answers the visitors. The portal's built-in agent cannot be used.{' '}
          <TextLink to="/agents" state={returnState}>
            Manage agents
          </TextLink>
        </>
      }
    >
      <Select
        {...fieldAria(controlId, errors, true)}
        value={value}
        onChange={(agentId) => kit.set('agentId', agentId)}
        placeholder="Choose an agent"
        options={[
          ...agents.map((agent) => ({ value: agent.id, label: agent.name })),
          ...(value && !known ? [{ value, label: 'Unknown agent' }] : []),
        ]}
      />
    </Field>
  );
}

function NoticeView({ notice }: { notice: Notice }) {
  const returnState = useReturnState();
  return (
    <Alert tone={notice.tone} title={notice.title}>
      {notice.text}
      {notice.link ? (
        <>
          {' '}
          <TextLink to={notice.link.to} state={returnState}>
            {notice.link.label}
          </TextLink>
        </>
      ) : null}
    </Alert>
  );
}

function Control({ property, ...props }: { property: PropertyDef } & Omit<ControlProps<PropertyDef['kind']>, 'property'>) {
  switch (property.kind) {
    case 'preset':
      return <PresetControl property={property} {...props} />;
    case 'colorToken':
      return <ColorTokenControl property={property} {...props} />;
    case 'text':
      return <TextControl property={property} {...props} />;
    case 'longText':
      return <LongTextControl property={property} {...props} />;
    case 'color':
      return <ColorControl property={property} {...props} />;
    case 'icon':
      return <IconControl property={property} {...props} />;
    case 'image':
      return <ImageControl property={property} {...props} />;
    case 'toggle':
      return <ToggleControl property={property} {...props} />;
    case 'choice':
      return <ChoiceControl property={property} {...props} />;
    case 'list':
      return <ListControl property={property} {...props} />;
    case 'number':
      return <NumberControl property={property} {...props} />;
    case 'agent':
      return <AgentControl property={property} {...props} />;
    case 'timeZone':
      return <TimeZoneControl property={property} {...props} />;
    case 'hours':
      return <HoursControl property={property} {...props} />;
  }
}

function EditedMarker({ property, kit }: { property: PropertyDef; kit: EditorKit }) {
  return (
    <button
      type="button"
      onClick={() => kit.set(property.id, readPath(kit.saved, property.id))}
      aria-label={`Edited. Reset ${property.label.toLowerCase()} to the saved value`}
      title="Reset to the saved value"
      className="inline-flex shrink-0 items-center gap-1 rounded-full px-1.5 text-xs font-medium text-warning-text hover:bg-warning-subtle"
    >
      <span aria-hidden className="size-1.5 rounded-full bg-warning" />
      <RotateCcw className="size-3" aria-hidden />
    </button>
  );
}

export function PropertyField({ property, kit }: { property: PropertyDef; kit: EditorKit }) {
  if (property.when && !property.when(kit.values)) {
    return null;
  }
  const notice = property.notice?.(kit.values, { integrations: kit.integrations, agents: kit.agents }) ?? null;
  const edited = property.kind !== 'colorToken' && !samePath(kit.values, kit.saved, property.id);
  return (
    <div className="space-y-2">
      <Control
        property={property}
        kit={kit}
        controlId={controlIdOf(kit.id, property.id)}
        errors={property.field ? kit.errorsFor(property.field) : undefined}
        action={edited ? <EditedMarker property={property} kit={kit} /> : undefined}
      />
      {notice ? <NoticeView notice={notice} /> : null}
    </div>
  );
}
