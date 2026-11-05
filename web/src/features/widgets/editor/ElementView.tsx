import { clsx } from 'clsx';
import { ChevronLeft } from 'lucide-react';
import { useEffect, useId, useRef } from 'react';
import { Button } from '../../../components/ui/Button';
import { Alert } from '../../../components/ui/feedback';
import type { PreviewScene } from '../../../widget/shared/preview';
import { PropertyField, type EditorKit } from './controls';
import { inMode, isColorPath, type DraftPath } from './draft';
import { SCENE_LABELS, type Registry, type TargetDef } from './registry';

export function ElementView({
  element,
  registry,
  kit,
  scene,
  notShown,
  focusId,
  onBack,
  onOpenTheme,
  onShowLauncher,
}: {
  element: TargetDef;
  registry: Registry;
  kit: EditorKit;
  scene: PreviewScene;
  notShown: boolean;
  focusId: string | null;
  onBack: () => void;
  onOpenTheme: () => void;
  onShowLauncher: () => void;
}) {
  const headingRef = useRef<HTMLHeadingElement>(null);
  const colorsId = useId();
  const properties = element.properties.filter((id) => inMode(id, kit.mode));
  const others = properties.filter((id) => !isColorPath(id));
  const colors = properties.filter(isColorPath);
  const other = kit.mode === 'light' ? 'dark' : 'light';

  useEffect(() => {
    ((focusId ? document.getElementById(focusId) : null) ?? headingRef.current)?.focus();
  }, [focusId]);

  const field = (id: DraftPath) => {
    const property = registry.properties.get(id);
    return property ? <PropertyField key={id} property={property} kit={kit} /> : null;
  };

  return (
    <div
      className="flex h-full min-h-0 flex-col"
      onKeyDown={(event) => {
        if (event.key === 'Escape') {
          event.stopPropagation();
          onBack();
        }
      }}
    >
      <div className="flex shrink-0 items-center gap-1.5 border-b border-border-subtle py-2 pr-3 pl-1.5">
        <Button variant="ghost" size="icon-sm" icon={ChevronLeft} aria-label="All elements" title="All elements" onClick={onBack} />
        <div className="min-w-0 flex-1">
          <h2 ref={headingRef} tabIndex={-1} className="truncate text-sm font-semibold text-fg focus:outline-none">
            {element.label}
          </h2>
          <p className="truncate text-xs text-fg-muted">
            Scene: {SCENE_LABELS[element.scene]}
            {notShown ? ', not shown now' : ''}
          </p>
        </div>
      </div>
      <div className="min-h-0 flex-1 space-y-4 overflow-y-auto p-4">
        {element.id === 'appearance.launcher' && scene !== 'launcher' ? (
          <Alert
            tone="info"
            action={
              <Button size="sm" onClick={onShowLauncher}>
                Show closed
              </Button>
            }
          >
            The chat is open, so the launcher shows its close icon.
          </Alert>
        ) : null}
        {notShown ? <p className="text-xs text-fg-muted">{element.label} does not show in the preview now.</p> : null}
        {others.map(field)}
        {colors.length > 0 ? (
          <section aria-labelledby={colorsId} className={clsx('space-y-2', others.length > 0 && 'border-t border-border-subtle pt-4')}>
            <div className="flex items-baseline justify-between gap-2">
              <h3 id={colorsId} className="text-xs font-medium text-fg-muted">
                Colors
              </h3>
              <span className="text-xs text-fg-muted">{kit.mode === 'dark' ? 'Dark mode' : 'Light mode'}</span>
            </div>
            <div className="-mx-1.5 space-y-0.5">{colors.map(field)}</div>
            <p className="text-xs text-fg-muted">
              {kit.values.theme === 'auto'
                ? `The ${kit.mode} colors, as the preview shows; switch it to ${other} for the ${other} ones.`
                : `The widget is always ${kit.mode}.`}{' '}
              Brand color, theme and mode are in{' '}
              <button type="button" onClick={onOpenTheme} className="rounded-sm font-medium text-accent-text underline-offset-2 hover:underline">
                Theme
              </button>
              .
            </p>
          </section>
        ) : null}
      </div>
    </div>
  );
}
