import { clsx } from 'clsx';
import { CircleAlert, FlaskConical, Redo2, RotateCcw, Save, Trash2, Undo2 } from 'lucide-react';
import { Badge } from '../../../components/ui/Badge';
import { Button } from '../../../components/ui/Button';
import { Menu } from '../../../components/ui/Menu';
import type { WidgetDto } from '../types';
import type { WidgetField } from '../widgetForm';

export interface EditorProblem {
  field: WidgetField;
  label: string;
  message: string;
}

export interface EditorHeaderProps {
  widget: WidgetDto;
  notice: string | null;
  dirty: boolean;
  problems: readonly EditorProblem[];
  onReveal: (field: WidgetField) => void;
  canUndo: boolean;
  canRedo: boolean;
  onUndo: () => void;
  onRedo: () => void;
  onDelete: () => void;
  onTest: () => void;
  onDiscard: () => void;
  saving: boolean;
  canSave: boolean;
  onSave: () => void;
}

export const shortcut = (key: string) => `${/Mac|iPhone|iPad/.test(navigator.userAgent) ? 'Meta' : 'Control'}+${key}`;

export function EditorHeader({
  widget,
  notice,
  dirty,
  problems,
  onReveal,
  canUndo,
  canRedo,
  onUndo,
  onRedo,
  onDelete,
  onTest,
  onDiscard,
  saving,
  canSave,
  onSave,
}: EditorHeaderProps) {
  return (
    <header className="@container flex min-h-12 shrink-0 flex-wrap items-center gap-x-3 gap-y-2 px-4 py-2 lg:px-6">
      <div className="flex min-w-0 flex-1 items-center gap-2.5">
        <h1 className="truncate text-section text-fg">{widget.name}</h1>
        <Badge tone={widget.enabled ? 'success' : 'neutral'} dot>
          {widget.enabled ? 'Enabled' : 'Disabled'}
        </Badge>
        <code className="hidden truncate font-mono text-xs text-fg-muted 2xl:inline">{widget.publicKey}</code>
      </div>
      <div className="flex flex-wrap items-center gap-1.5">
        <span role="status" className={clsx('text-xs', notice ? 'max-w-xs text-success-text' : 'text-fg-muted')}>
          {notice ?? (dirty ? 'Unsaved changes' : '')}
        </span>
        {problems.length > 0 ? (
          <Menu
            label="Settings to fix"
            className="w-80"
            trigger={(props) => (
              <button
                {...props}
                type="button"
                className="inline-flex h-7 items-center gap-1.5 rounded-md bg-danger-subtle px-2.5 text-small font-medium text-danger-text ring-danger/40 ring-inset hover:ring-1"
              >
                <CircleAlert className="size-3.5" aria-hidden />
                {problems.length === 1 ? '1 to fix' : `${problems.length} to fix`}
              </button>
            )}
            items={problems.map((problem) => ({
              label: (
                <span className="text-small text-fg-secondary">
                  <span className="font-medium text-fg">{problem.label}:</span> {problem.message}
                </span>
              ),
              onSelect: () => onReveal(problem.field),
            }))}
          />
        ) : null}
        <Button
          variant="ghost"
          size="icon-sm"
          icon={Undo2}
          aria-label="Undo"
          aria-keyshortcuts={shortcut('Z')}
          title="Undo"
          disabled={!canUndo}
          onClick={onUndo}
        />
        <Button
          variant="ghost"
          size="icon-sm"
          icon={Redo2}
          aria-label="Redo"
          aria-keyshortcuts={shortcut('Shift+Z')}
          title="Redo"
          disabled={!canRedo}
          onClick={onRedo}
        />
        <Menu label="More actions for this widget" items={[{ label: 'Delete widget', icon: Trash2, danger: true, onSelect: onDelete }]} />
        <Button
          size="sm"
          icon={FlaskConical}
          title={dirty ? 'Tries the saved settings. Save first to try your changes.' : 'Try the real widget as visitors get it'}
          onClick={onTest}
        >
          <span className="@max-[36rem]:sr-only">Test this widget</span>
        </Button>
        <Button size="sm" icon={RotateCcw} title="Discard" disabled={!dirty || saving} onClick={onDiscard}>
          <span className="@max-[36rem]:sr-only">Discard</span>
        </Button>
        <Button
          size="sm"
          variant="primary"
          icon={Save}
          aria-keyshortcuts={shortcut('S')}
          loading={saving}
          disabled={!canSave}
          onClick={onSave}
        >
          Save
        </Button>
      </div>
    </header>
  );
}
