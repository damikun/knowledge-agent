import { useId, useState } from 'react';
import { ApiError } from '../../lib/api';
import { useFocusFirstInvalid } from '../../lib/hooks/useFocusFirstInvalid';
import { keyFromName, type AgentFormErrors, type AgentFormField, type AgentFormValues } from './agentForm';
import type { FieldKit } from './agentFields';

const INVALID_CONTROL = '[aria-invalid="true"] input, [aria-invalid="true"]:not([role="radiogroup"])';

function withoutErrors(errors: AgentFormErrors, fields: readonly AgentFormField[]): AgentFormErrors {
  const next = { ...errors };
  for (const field of fields) {
    delete next[field];
  }
  return next;
}

export interface AgentFormState {
  kit: FieldKit;
  values: AgentFormValues;
  setValues: (values: AgentFormValues) => void;
  clientErrors: AgentFormErrors;
  setClientErrors: (errors: AgentFormErrors) => void;
  /** Field errors the API sent back, outside `shownFields`, listed together. */
  otherErrors: (shownFields: readonly AgentFormField[]) => string[];
  focusInvalid: () => void;
  formRef: React.RefObject<HTMLFormElement | null>;
}

/** A new agent's key follows its name until the key is typed. */
export function useAgentForm(initial: AgentFormValues, { isNew, serverError }: { isNew: boolean; serverError: unknown }): AgentFormState {
  const idPrefix = useId();
  const [values, setValues] = useState(initial);
  const [clientErrors, setClientErrors] = useState<AgentFormErrors>({});
  const [focusRequest, setFocusRequest] = useState(0);
  const validation = serverError instanceof ApiError && serverError.isValidation ? serverError : null;
  // A radio group marked invalid hands the focus to its first option.
  const formRef = useFocusFirstInvalid(focusRequest, validation, INVALID_CONTROL);

  const set: FieldKit['set'] = (field, value, clears = []) => {
    setValues((current) => {
      const next = { ...current, [field]: value };
      if (field === 'name' && isNew && !current.keyEdited) {
        next.key = keyFromName(value as string);
      }
      if (field === 'key') {
        next.keyEdited = value !== '';
      }
      return next;
    });
    if (clears.length > 0) {
      setClientErrors((current) => withoutErrors(current, clears));
    }
  };

  const errorsFor = (field: AgentFormField): string[] | undefined => {
    const client = clientErrors[field];
    if (client) {
      return [client];
    }
    const server = validation?.errorsFor(field) ?? [];
    return server.length > 0 ? server : undefined;
  };

  return {
    kit: { values, set, errorsFor, id: (name) => `${idPrefix}-${name}` },
    values,
    setValues,
    clientErrors,
    setClientErrors,
    otherErrors: (shownFields) => (validation ? validation.errorsExcept(shownFields) : []),
    focusInvalid: () => setFocusRequest((count) => count + 1),
    formRef,
  };
}
