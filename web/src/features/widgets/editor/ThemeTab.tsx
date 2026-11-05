import { RotateCcw } from 'lucide-react';
import { Button } from '../../../components/ui/Button';
import { PropertyField, type EditorKit } from './controls';
import type { DraftPath } from './draft';
import type { Registry } from './registry';

const COLOR_GROUPS: ReadonlyArray<{ label: string; tokens: readonly string[] }> = [
  { label: 'Panel', tokens: ['panel', 'text', 'muted', 'border'] },
  { label: 'Accent', tokens: ['accent', 'onAccent'] },
  { label: 'Header', tokens: ['header', 'headerText'] },
  { label: 'Messages', tokens: ['userBubble', 'userBubbleText', 'assistantBubble', 'assistantBubbleText'] },
  { label: 'Launcher', tokens: ['launcher', 'launcherIcon', 'glow'] },
];

export function ThemeTab({ registry, kit, onResetColors }: { registry: Registry; kit: EditorKit; onResetColors: () => void }) {
  const field = (id: DraftPath) => {
    const property = registry.properties.get(id);
    return property ? <PropertyField key={id} property={property} kit={kit} /> : null;
  };
  const other = kit.mode === 'light' ? 'dark' : 'light';
  const overridden = Object.values(kit.values.colors[kit.mode]).some((color) => color.trim() !== '');
  return (
    <div className="space-y-5 p-4">
      {field('preset')}
      {field('brandColor')}
      {field('theme')}
      <div className="space-y-3 border-t border-border-subtle pt-4">
        <div className="flex items-start justify-between gap-2">
          <div>
            <h2 className="text-sm font-semibold text-fg">Colors in {kit.mode} mode</h2>
            <p className="mt-0.5 text-xs text-fg-muted">
              {kit.values.theme === 'auto'
                ? `As the preview shows. Switch it to ${other} to change the ${other} colors.`
                : `The widget is always ${kit.mode}.`}{' '}
              Empty fields keep the theme's.
            </p>
          </div>
          <Button size="sm" variant="ghost" icon={RotateCcw} disabled={!overridden} onClick={onResetColors}>
            All to theme
          </Button>
        </div>
        {COLOR_GROUPS.map((group) => (
          <div key={group.label} className="space-y-2.5">
            <h3 className="text-xs font-medium text-fg-muted">{group.label}</h3>
            {group.tokens.map((token) => field(`colors.${kit.mode}.${token}`))}
          </div>
        ))}
      </div>
    </div>
  );
}
