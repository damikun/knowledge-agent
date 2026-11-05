import { CircleAlert } from 'lucide-react';
import type { EditTarget, PreviewScene } from '../../../widget/shared/preview';
import { AREA_LABELS, SCENE_LABELS, type Registry, type TargetArea, type TargetDef } from './registry';

/** The preview's elements as a list, for keyboard users and for elements the preview doesn't show. */
export function Outline({
  registry,
  scene,
  shown,
  invalid,
  edited,
  onSelect,
  itemId,
}: {
  registry: Registry;
  scene: PreviewScene;
  shown: ReadonlySet<EditTarget> | null;
  invalid: (target: TargetDef) => boolean;
  edited: (target: TargetDef) => boolean;
  onSelect: (target: EditTarget) => void;
  itemId: (target: EditTarget) => string;
}) {
  const areas = Object.keys(AREA_LABELS) as TargetArea[];

  return (
    <div className="space-y-3">
      <p className="text-xs text-fg-muted">Click an element in the preview, or pick it here. Elements of other scenes switch the scene.</p>
      {areas.map((area) => {
        const targets = registry.targets.filter((target) => target.area === area && target.properties.length > 0);
        return (
          <div key={area}>
            <h2 className="mb-1 px-2.5 text-xs font-medium text-fg-muted">{AREA_LABELS[area]}</h2>
            <ul className="space-y-0.5">
              {targets.map((target) => (
                <li key={target.id}>
                  <button
                    id={itemId(target.id)}
                    type="button"
                    onClick={() => onSelect(target.id)}
                    className="flex min-h-8 w-full items-center gap-2 rounded-md px-2.5 py-1 text-left text-sm text-fg-secondary hover:bg-fill-hover hover:text-fg"
                  >
                    <span className="flex-1 truncate">{target.label}</span>
                    {shown && !shown.has(target.id) ? (
                      <span className="text-xs text-fg-muted">{target.scene === scene ? 'Not shown' : SCENE_LABELS[target.scene]}</span>
                    ) : null}
                    {edited(target) ? (
                      <>
                        <span aria-hidden className="size-1.5 shrink-0 rounded-full bg-warning" />
                        <span className="sr-only">, edited</span>
                      </>
                    ) : null}
                    {invalid(target) ? (
                      <>
                        <CircleAlert className="size-3.5 shrink-0 text-danger" aria-hidden />
                        <span className="sr-only">, needs a fix</span>
                      </>
                    ) : null}
                  </button>
                </li>
              ))}
            </ul>
          </div>
        );
      })}
    </div>
  );
}
