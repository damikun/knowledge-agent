import { useMutation } from '@tanstack/react-query';
import { useId, useState, type FormEvent } from 'react';
import { Button } from '../../components/ui/Button';
import { Alert, ErrorMessage } from '../../components/ui/feedback';
import { SideSheet } from '../../components/ui/SideSheet';
import { ApiError } from '../../lib/api';
import { evalsApi } from './api';
import { EMPTY_QUESTION, QUESTION_FIELDS, QuestionFields, draftOf, toRequest, type QuestionDraft } from './QuestionFields';
import type { EvalDatasetItemDto } from './types';

export interface QuestionSheetProps {
  datasetId: string;
  /** Null to add a new question. */
  item: EvalDatasetItemDto | null;
  onClose: () => void;
  onSaved: (item: EvalDatasetItemDto) => void;
}

export function QuestionSheet({ datasetId, item, onClose, onSaved }: QuestionSheetProps) {
  const idPrefix = useId();
  const formId = `${idPrefix}-form`;
  const [draft, setDraft] = useState<QuestionDraft>(item ? draftOf(item) : EMPTY_QUESTION);
  const save = useMutation({
    // The API ignores a changed source on update anyway.
    mutationFn: () =>
      item ? evalsApi.updateItem(datasetId, item.id, toRequest(draft)) : evalsApi.addItem(datasetId, toRequest(draft, { source: 'manual' })),
    onSuccess: (saved) => {
      onSaved(saved);
      onClose();
    },
  });
  const other = save.error instanceof ApiError ? save.error.errorsExcept(QUESTION_FIELDS) : [];
  const submit = (event: FormEvent) => {
    event.preventDefault();
    save.mutate();
  };

  return (
    <SideSheet
      open
      onClose={onClose}
      size="lg"
      title={item ? 'Edit question' : 'Add a question'}
      description={item ? 'Changes apply from the next run.' : 'Type a question a visitor could ask, and what a good answer says.'}
      footer={
        <>
          <Button onClick={onClose} disabled={save.isPending}>
            Cancel
          </Button>
          <Button type="submit" form={formId} variant="primary" loading={save.isPending}>
            {item ? 'Save' : 'Add question'}
          </Button>
        </>
      }
    >
      <form id={formId} onSubmit={submit} noValidate className="space-y-4">
        <QuestionFields idPrefix={idPrefix} draft={draft} onChange={setDraft} error={save.error} />
        {save.error && !(save.error instanceof ApiError && save.error.isValidation) ? <ErrorMessage error={save.error} title="The question was not saved" /> : null}
        {other.length > 0 ? (
          <Alert tone="danger" title="The question was not saved" live>
            {other.join(' ')}
          </Alert>
        ) : null}
      </form>
    </SideSheet>
  );
}
