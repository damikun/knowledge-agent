import type { ReactNode } from 'react';
import type { Guid } from '../../../lib/types';
import type { AgentDto } from '../../agents/types';
import type { IntegrationsDto } from '../../notifications/types';
import type { ResolvedTheme } from '../../../widget/shared/theme';
import type { ColorMode, WidgetField, WidgetFormValues } from '../widgetForm';
import type { SetPath } from './draft';
import type { PropertyDef } from './registry';

export interface EditorKit {
  values: WidgetFormValues;
  saved: WidgetFormValues;
  set: SetPath;
  errorsFor: (field: WidgetField) => string[] | undefined;
  mode: ColorMode;
  theme: ResolvedTheme;
  id: (name: string) => string;
  agents: readonly AgentDto[];
  integrations: IntegrationsDto | undefined;
  widgetId: Guid | null;
}

export type Of<K extends PropertyDef['kind']> = Extract<PropertyDef, { kind: K }>;

export interface ControlProps<K extends PropertyDef['kind']> {
  property: Of<K>;
  kit: EditorKit;
  controlId: string;
  errors: string[] | undefined;
  action?: ReactNode;
}

export function ControlLabel({
  as: Tag = 'span',
  id,
  action,
  children,
}: {
  as?: 'span' | 'legend' | 'div';
  id?: string;
  action?: ReactNode;
  children: ReactNode;
}) {
  const label = (
    <Tag id={id} className="text-small font-medium text-fg">
      {children}
    </Tag>
  );
  return action ? (
    <div className="flex items-baseline justify-between gap-2">
      {label}
      {action}
    </div>
  ) : (
    label
  );
}

export function FieldMessage({ id, errors, hint }: { id: string; errors?: string[] | undefined; hint?: ReactNode }) {
  if (errors && errors.length > 0) {
    return (
      <p id={`${id}-error`} className="mt-1.5 text-xs text-danger-text">
        {errors.join(' ')}
      </p>
    );
  }
  return hint ? (
    <p id={`${id}-hint`} className="mt-1.5 text-xs text-fg-muted">
      {hint}
    </p>
  ) : null;
}

export function describedBy(id: string, errors: string[] | undefined, hint: unknown): string | undefined {
  return errors?.length ? `${id}-error` : hint ? `${id}-hint` : undefined;
}
