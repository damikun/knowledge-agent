import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useId, useState, type FormEvent } from 'react';
import { fieldAria } from '../../components/ui/aria';
import { Button } from '../../components/ui/Button';
import { Alert, ErrorMessage } from '../../components/ui/feedback';
import { Field } from '../../components/ui/Field';
import { Modal } from '../../components/ui/Modal';
import { toast } from '../../components/ui/Toast';
import { ApiError } from '../../lib/api';
import { pluralize } from '../../lib/format';
import { evalKeys, evalsApi } from './api';
import type { EvalDatasetDto } from './types';

export type DatasetFormMode = { kind: 'create' } | { kind: 'rename'; dataset: EvalDatasetDto } | { kind: 'copy'; from: EvalDatasetDto };

export interface DatasetFormDialogProps {
  mode: DatasetFormMode;
  onClose: () => void;
  onSaved?: (dataset: EvalDatasetDto) => void;
}

const TITLES = { create: 'New dataset', rename: 'Rename dataset', copy: 'Make a copy' } as const;

export function DatasetFormDialog({ mode, onClose, onSaved }: DatasetFormDialogProps) {
  const queryClient = useQueryClient();
  const idPrefix = useId();
  const [name, setName] = useState(mode.kind === 'rename' ? mode.dataset.name : mode.kind === 'copy' ? `${mode.from.name} copy` : '');
  const [description, setDescription] = useState(mode.kind === 'rename' ? (mode.dataset.description ?? '') : '');

  const save = useMutation({
    mutationFn: () => {
      if (mode.kind === 'rename') {
        return evalsApi.updateDataset(mode.dataset.id!, { name: name.trim(), description: description.trim() });
      }
      return evalsApi.createDataset({
        name: name.trim(),
        description: description.trim() || null,
        copyFrom: mode.kind === 'copy' ? mode.from.name : null,
        copyFromSource: mode.kind === 'copy' ? mode.from.source : null,
      });
    },
    onSuccess: (saved) => {
      void queryClient.invalidateQueries({ queryKey: evalKeys.datasets });
      toast({ title: mode.kind === 'rename' ? 'Dataset saved' : `Created ${saved.name}`, tone: 'success' });
      onSaved?.(saved);
      onClose();
    },
  });

  const errors = (field: string) => (save.error instanceof ApiError ? save.error.errorsFor(field) : []);
  const other = save.error instanceof ApiError ? save.error.errorsExcept(['name', 'description']) : [];
  const formId = `${idPrefix}-form`;
  const submit = (event: FormEvent) => {
    event.preventDefault();
    save.mutate();
  };

  return (
    <Modal
      open
      onClose={onClose}
      size="sm"
      title={TITLES[mode.kind]}
      description={
        mode.kind === 'copy'
          ? `Starts with the ${pluralize(mode.from.itemCount, 'question')} of ${mode.from.name}, which you can then edit. It runs against your own sources.`
          : mode.kind === 'create'
            ? 'Add questions next, by hand or from conversations.'
            : undefined
      }
      dismissible={!save.isPending}
      footer={
        <>
          <Button onClick={onClose} disabled={save.isPending}>
            Cancel
          </Button>
          <Button type="submit" form={formId} variant="primary" loading={save.isPending}>
            {mode.kind === 'rename' ? 'Save' : 'Create dataset'}
          </Button>
        </>
      }
    >
      <form id={formId} onSubmit={submit} noValidate className="space-y-4">
        <Field label="Name" htmlFor={`${idPrefix}-name`} errors={errors('name')} hint="Letters, digits, spaces, dashes and dots.">
          <input
            {...fieldAria(`${idPrefix}-name`, errors('name'), true)}
            autoComplete="off"
            value={name}
            onChange={(event) => setName(event.target.value)}
            placeholder="Shipping questions"
            className="field"
          />
        </Field>
        <Field label="Description" htmlFor={`${idPrefix}-description`} optional errors={errors('description')}>
          <textarea
            {...fieldAria(`${idPrefix}-description`, errors('description'))}
            rows={2}
            value={description}
            onChange={(event) => setDescription(event.target.value)}
            placeholder={mode.kind === 'copy' ? 'Kept from the original when empty' : 'What these questions check'}
            className="field min-h-16 py-1.5"
          />
        </Field>
        {save.isError && !(save.error instanceof ApiError && save.error.isValidation) ? <ErrorMessage error={save.error} title="The dataset was not saved" /> : null}
        {other.length > 0 ? (
          <Alert tone="danger" title="The dataset was not saved" live>
            {other.join(' ')}
          </Alert>
        ) : null}
      </form>
    </Modal>
  );
}
