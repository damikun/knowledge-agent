/** Links a control to the hint or error of the Field it is rendered in. */
export function fieldAria(id: string, errors?: string[], hasHint = false) {
  const invalid = Boolean(errors && errors.length > 0);
  return {
    id,
    'aria-invalid': invalid || undefined,
    'aria-describedby': invalid ? `${id}-error` : hasHint ? `${id}-hint` : undefined,
  };
}
