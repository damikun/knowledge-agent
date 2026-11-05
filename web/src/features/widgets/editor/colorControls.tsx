import { clsx } from 'clsx';
import { RotateCcw, TriangleAlert } from 'lucide-react';
import { useRef, useState, type RefObject } from 'react';
import { Button } from '../../../components/ui/Button';
import { Alert } from '../../../components/ui/feedback';
import { Field, inputProps } from '../../../components/ui/Field';
import { fieldAria } from '../../../components/ui/aria';
import { Popover } from '../../../components/ui/Popover';
import { contrastRatio, isHexColor, MIN_TEXT_CONTRAST, readableTextColor, WHITE } from '../../../lib/color';
import { contrastProblems, resolveTheme, themeColors, TRANSPARENT, type ColorToken, type ResolvedTheme } from '../../../widget/shared/theme';
import { COLOR_LABELS, PRESET_LABELS } from '../widgetTexts';
import { ControlLabel, describedBy, FieldMessage, type ControlProps, type EditorKit, type Of } from './controlParts';
import { readPath } from './draft';

export function ColorControl({ property, kit, controlId, errors, action }: ControlProps<'color'>) {
  const color = kit.values.brandColor.trim().toLowerCase();
  const valid = isHexColor(color);
  // The widget uses white or near-black text on the brand color and shifts the color when neither reaches 4.5:1.
  const text = valid ? readableTextColor(color) : null;
  const contrast = text ? contrastRatio(text, color) : null;
  const low = contrast !== null && contrast < MIN_TEXT_CONTRAST;
  const set = (value: string) => kit.set('brandColor', value);
  return (
    <div className="space-y-2">
      <Field label={property.label} htmlFor={controlId} errors={errors} hint={property.hint} action={action}>
        <div className="flex items-center gap-2">
          <input
            type="color"
            aria-label={`Pick the ${property.label.toLowerCase()}`}
            value={valid ? color : '#000000'}
            onChange={(event) => set(event.target.value)}
            className="h-8 w-11 shrink-0 cursor-pointer rounded-md border border-border-strong bg-surface p-1 shadow-control"
          />
          <input
            {...fieldAria(controlId, errors, Boolean(property.hint))}
            {...inputProps}
            maxLength={7}
            placeholder="#0f766e"
            value={kit.values.brandColor}
            onChange={(event) => set(event.target.value)}
            className="field h-8 py-0 font-mono text-xs"
          />
          {valid && text ? (
            <span aria-hidden className="flex h-8 shrink-0 items-center rounded-md px-2.5 text-sm font-semibold" style={{ backgroundColor: color, color: text }}>
              Aa
            </span>
          ) : null}
        </div>
      </Field>
      {low ? (
        <Alert tone="warning" title="Text on this color is hard to read">
          {text === WHITE ? 'White' : 'Dark'} text on {color} reaches only {contrast.toFixed(2)}:1, below {MIN_TEXT_CONTRAST}:1. The widget{' '}
          {text === WHITE ? 'darkens' : 'lightens'} the color a little behind text, or pick another one.
        </Alert>
      ) : null}
    </div>
  );
}

function PresetSwatch({ theme }: { theme: ResolvedTheme }) {
  const v = theme.variables;
  return (
    <span
      aria-hidden
      className="relative block h-16 overflow-hidden rounded-xl bg-[linear-gradient(135deg,#6366f1,#ec4899_55%,#14b8a6)]"
    >
      <span
        className="absolute inset-y-2 right-2 left-6 flex flex-col gap-1 rounded-lg p-1.5 shadow-sm"
        style={{ background: v['--ka-panel-fill'], backdropFilter: theme.effects.glass ? 'blur(6px)' : undefined }}
      >
        <span className="h-1.5 w-8 rounded-full" style={{ background: theme.colors.header === TRANSPARENT ? v['--ka-text'] : v['--ka-header'] }} />
        <span className="h-2.5 w-10 rounded-full" style={{ background: v['--ka-assistant-bubble'] }} />
        <span
          className="ml-auto h-2.5 w-9 rounded-full"
          style={{ background: `linear-gradient(135deg, ${v['--ka-user-bubble']}, ${v['--ka-user-bubble-end']})` }}
        />
      </span>
      <span
        className="absolute bottom-2 left-2 size-3 rounded-full"
        style={{
          background: `linear-gradient(135deg, ${v['--ka-launcher']}, ${v['--ka-launcher-end']})`,
          boxShadow: theme.effects.glow ? `0 0 8px 2px ${v['--ka-glow-2']}` : undefined,
        }}
      />
    </span>
  );
}

export function PresetControl({ property, kit, controlId, errors, action }: ControlProps<'preset'>) {
  const current = kit.values.preset;
  const focusable = property.choices.includes(current) ? current : property.choices[0];
  return (
    <fieldset aria-describedby={describedBy(controlId, errors, false)}>
      <ControlLabel as="legend" action={action}>
        {property.label}
      </ControlLabel>
      <div className="mt-1.5 grid grid-cols-3 gap-2">
        {property.choices.map((preset) => {
          const meta = PRESET_LABELS[preset];
          const theme = resolveTheme({ preset, brandColor: kit.values.brandColor, mode: kit.mode });
          return (
            <label key={preset} className="relative flex cursor-pointer">
              <input
                type="radio"
                id={preset === focusable ? controlId : undefined}
                name={controlId}
                checked={current === preset}
                onChange={() => kit.set('preset', preset)}
                className="peer sr-only"
              />
              <span
                className={clsx(
                  // Full height, so the cards line up when one description wraps.
                  'flex w-full flex-col gap-1.5 rounded-2xl border p-1.5 peer-focus-visible:outline-2 peer-focus-visible:outline-offset-2 peer-focus-visible:outline-focus',
                  current === preset ? 'border-accent ring-1 ring-accent' : 'border-border hover:border-border-hover',
                )}
              >
                <PresetSwatch theme={theme} />
                <span className="block px-0.5 text-small font-medium text-fg">{meta?.label ?? preset}</span>
                <span className="block px-0.5 pb-0.5 text-xs text-fg-muted">{meta?.description}</span>
              </span>
            </label>
          );
        })}
      </div>
      <FieldMessage id={controlId} errors={errors} />
    </fieldset>
  );
}

function contrastNote(theme: ResolvedTheme, token: ColorToken): string | null {
  const problem = contrastProblems(theme).find((candidate) => candidate.text === token || candidate.background === token);
  if (!problem) {
    return null;
  }
  const ratio = problem.ratio.toFixed(2);
  return problem.text === token
    ? `Reaches only ${ratio}:1 on ${(COLOR_LABELS[problem.background] ?? problem.background).toLowerCase()}, below 4.5:1.`
    : `${COLOR_LABELS[problem.text] ?? problem.text} on it reaches only ${ratio}:1, below 4.5:1.`;
}

function Swatch({ color, className }: { color: string; className?: string }) {
  return (
    <span
      aria-hidden
      className={clsx(
        'block shrink-0 rounded-md ring-1 ring-swatch-edge ring-inset',
        color === TRANSPARENT && 'bg-[repeating-conic-gradient(#d4d4d8_0%_25%,#ffffff_0%_50%)] bg-size-[8px_8px]',
        className,
      )}
      style={color === TRANSPARENT ? undefined : { backgroundColor: color }}
    />
  );
}

function swatchesOf(kit: EditorKit): string[] {
  const candidates = [kit.values.brandColor, ...Object.values(kit.theme.colors), ...Object.values(kit.values.colors.light), ...Object.values(kit.values.colors.dark)];
  const unique = new Set(candidates.map((color) => color.trim().toLowerCase()).filter((color) => isHexColor(color)));
  return [...unique].slice(0, 16);
}

function ColorPicker({ property, kit, anchorRef, open, onClose }: { property: Of<'colorToken'>; kit: EditorKit; anchorRef: RefObject<HTMLButtonElement | null>; open: boolean; onClose: () => void }) {
  const hexRef = useRef<HTMLInputElement>(null);
  const override = readPath(kit.values, property.id);
  const own = themeColors(kit.theme.preset, kit.values.brandColor, property.mode)[property.token];
  const shown = kit.theme.colors[property.token];
  const invalid = override.trim() !== '' && !isHexColor(override.trim());
  const note = contrastNote(kit.theme, property.token);
  const messageId = `${kit.id(property.id)}-picker-message`;
  const set = (value: string) => kit.set(property.id, value);
  return (
    <Popover open={open} onClose={onClose} anchorRef={anchorRef} label={`${property.label} color`} align="end" className="w-72 space-y-3 p-3" initialFocus={hexRef}>
      <div className="flex items-center gap-2">
        {/* Ring on keyboard focus only, so a click leaves none. */}
        <label className="relative size-8 shrink-0 cursor-pointer overflow-hidden rounded-md has-focus-visible:outline-2 has-focus-visible:outline-offset-2 has-focus-visible:outline-focus">
          <Swatch color={shown} className="size-full rounded-md" />
          <input
            type="color"
            aria-label="Any color"
            value={isHexColor(override.trim()) ? override.trim() : shown === TRANSPARENT ? kit.theme.colors.panel : shown}
            onChange={(event) => set(event.target.value)}
            className="absolute inset-0 size-full cursor-pointer opacity-0"
          />
        </label>
        <input
          ref={hexRef}
          {...inputProps}
          aria-label="Hex color"
          maxLength={7}
          value={override}
          placeholder={own === TRANSPARENT ? 'Glass' : own}
          aria-invalid={invalid || undefined}
          aria-describedby={invalid || note ? messageId : undefined}
          onChange={(event) => set(event.target.value)}
          className="field h-8 py-0 font-mono text-xs"
        />
      </div>
      <div role="group" aria-label="Swatches" className="grid grid-cols-8 gap-1.5">
        {swatchesOf(kit).map((color) => (
          <button
            key={color}
            type="button"
            aria-label={`Use ${color}`}
            title={color}
            onClick={() => set(color)}
            className="aspect-square rounded-md ring-1 ring-swatch-edge ring-inset transition-transform hover:scale-110"
            style={{ backgroundColor: color }}
          />
        ))}
      </div>
      {invalid ? (
        <p id={messageId} className="text-xs text-danger-text">
          Enter a color such as #0f766e.
        </p>
      ) : note ? (
        <p id={messageId} className="text-xs text-warning-text">
          {note}
        </p>
      ) : null}
      <div className="flex items-center justify-between gap-2 border-t border-border-subtle pt-2.5">
        <span className="flex min-w-0 items-center gap-1.5 text-xs text-fg-muted">
          <Swatch color={own} className="size-3.5" />
          Theme: <span className="truncate font-mono">{own === TRANSPARENT ? 'glass' : own}</span>
        </span>
        <Button size="sm" variant="ghost" icon={RotateCcw} disabled={override === ''} onClick={() => set('')}>
          Reset to theme
        </Button>
      </div>
    </Popover>
  );
}

export function ColorTokenControl({ property, kit, controlId }: ControlProps<'colorToken'>) {
  const [open, setOpen] = useState(false);
  const triggerRef = useRef<HTMLButtonElement>(null);
  const override = readPath(kit.values, property.id).trim();
  const invalid = override !== '' && !isHexColor(override);
  const note = contrastNote(kit.theme, property.token);
  const label = property.label;
  return (
    <div className="flex items-center gap-1">
      <button
        ref={triggerRef}
        id={controlId}
        type="button"
        aria-haspopup="dialog"
        aria-expanded={open}
        aria-label={`${label}: ${override === '' ? 'theme' : override}`}
        aria-describedby={note ? `${controlId}-note` : undefined}
        aria-invalid={invalid || undefined}
        onClick={() => setOpen((value) => !value)}
        className="flex min-h-9 min-w-0 flex-1 items-center gap-2.5 rounded-md px-1.5 text-left hover:bg-fill-hover"
      >
        <Swatch color={kit.theme.colors[property.token]} className="size-6" />
        <span className="min-w-0 flex-1 truncate text-sm text-fg">{label}</span>
        {note ? <TriangleAlert className="size-3.5 shrink-0 text-warning" aria-hidden /> : null}
        {override === '' ? (
          <span className="text-xs text-fg-muted">Theme</span>
        ) : (
          <span className={clsx('inline-flex items-center gap-1.5 font-mono text-xs', invalid ? 'text-danger-text' : 'text-fg-secondary')}>
            <span aria-hidden className="size-1.5 rounded-full bg-accent" />
            {override}
          </span>
        )}
      </button>
      {override === '' ? (
        <span aria-hidden className="size-8 shrink-0" />
      ) : (
        <Button
          variant="ghost"
          size="icon-sm"
          icon={RotateCcw}
          aria-label={`Reset ${label.toLowerCase()} to the theme`}
          title="Reset to the theme"
          onClick={() => kit.set(property.id, '')}
        />
      )}
      {note ? (
        <span id={`${controlId}-note`} className="sr-only">
          {note}
        </span>
      ) : null}
      <ColorPicker property={property} kit={kit} anchorRef={triggerRef} open={open} onClose={() => setOpen(false)} />
    </div>
  );
}
