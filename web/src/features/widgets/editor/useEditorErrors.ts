import { useMemo } from 'react';
import { ApiError } from '../../../lib/api';
import { validateWidget, WIDGET_FIELDS, type ColorMode, type WidgetField, type WidgetFormValues } from '../widgetForm';
import { readPath, type DraftPath } from './draft';
import type { EditorProblem } from './EditorHeader';
import type { Registry } from './registry';

export const TEXTS_FIELD: WidgetField = 'settings.behavior.texts';
// The API reports these for a whole group, so no single control owns them.
export const GROUP_FIELDS: Readonly<Partial<Record<WidgetField, { label: string; mode?: ColorMode }>>> = {
  'settings.behavior.texts': { label: 'Texts' },
  'settings.appearance.colors.light': { label: 'Colors in light mode', mode: 'light' },
  'settings.appearance.colors.dark': { label: 'Colors in dark mode', mode: 'dark' },
};
// Group errors last: they have no field of their own to focus.
export const ERROR_ORDER: readonly WidgetField[] = [
  ...WIDGET_FIELDS.filter((field) => !GROUP_FIELDS[field]),
  ...WIDGET_FIELDS.filter((field) => GROUP_FIELDS[field]),
];

interface EditorErrorsInput {
  registry: Registry;
  values: WidgetFormValues;
  attempted: boolean;
  /** The values last sent, so the API's errors stay until their field changes. */
  submitted: WidgetFormValues | null;
  error: unknown;
}

export function useEditorErrors({ registry, values, attempted, submitted, error }: EditorErrorsInput) {
  const validation = error instanceof ApiError && error.isValidation ? error : null;
  const clientErrors = useMemo(() => (attempted ? validateWidget(values) : {}), [attempted, values]);
  const fieldValue = (from: WidgetFormValues, field: WidgetField) =>
    field === TEXTS_FIELD
      ? JSON.stringify(from.texts)
      : GROUP_FIELDS[field]?.mode
        ? JSON.stringify(from.colors[GROUP_FIELDS[field].mode])
        : JSON.stringify([...registry.properties.values()].filter((property) => property.field === field).map((property) => readPath(from, property.id)));
  const errorsFor = (field: WidgetField): string[] | undefined => {
    const client = clientErrors[field];
    if (client) {
      return [client];
    }
    if (!validation || !submitted || fieldValue(values, field) !== fieldValue(submitted, field)) {
      return undefined;
    }
    const server = validation.errorsFor(field);
    return server.length > 0 ? server : undefined;
  };
  const propertyOf = (field: WidgetField) =>
    [...registry.properties.values()].find((candidate) => candidate.field === field && (!candidate.when || candidate.when(values)));

  const problems: EditorProblem[] = ERROR_ORDER.flatMap((field) => {
    const messages = errorsFor(field);
    return messages ? [{ field, label: GROUP_FIELDS[field]?.label ?? propertyOf(field)?.label ?? field, message: messages.join(' ') }] : [];
  });

  const errorCount = (properties: readonly string[]) =>
    properties.filter((id) => {
      const property = registry.properties.get(id as DraftPath);
      return property?.field && (!property.when || property.when(values)) && errorsFor(property.field);
    }).length;

  return {
    validation,
    errorsFor,
    otherErrors: validation ? validation.errorsExcept(WIDGET_FIELDS) : [],
    propertyOf,
    problems,
    errorCount,
  };
}
