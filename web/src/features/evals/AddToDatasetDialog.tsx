import { useMutation, useQueryClient } from '@tanstack/react-query';
import { ClipboardPlus } from 'lucide-react';
import { useId, useState, type FormEvent } from 'react';
import { useNavigate } from 'react-router';
import { fieldAria } from '../../components/ui/aria';
import { Button } from '../../components/ui/Button';
import { Alert, ErrorMessage, Spinner } from '../../components/ui/feedback';
import { Field } from '../../components/ui/Field';
import { Modal } from '../../components/ui/Modal';
import { Select } from '../../components/ui/Select';
import { toast } from '../../components/ui/Toast';
import { ApiError } from '../../lib/api';
import { useConversation } from '../chat/api';
import { isQuestion } from '../chat/timeline';
import { evalKeys, evalsApi, useEvalDatasets } from './api';
import { QuestionFields, QUESTION_FIELDS, toRequest, type QuestionDraft } from './QuestionFields';
import { datasetPath } from './datasetItems';
import { isCustomDataset } from './runs';

export interface DatasetQuestionSource {
  question: string;
  answer?: string | null;
  conversationId?: string | null;
  messageId?: string | null;
}

export interface AddToDatasetDialogProps {
  source: DatasetQuestionSource;
  onClose: () => void;
}

const NEW = '__new__';
const LAST_DATASET_KEY = 'ka.evals.lastDataset';

function lastDataset(): string | null {
  try {
    return window.localStorage.getItem(LAST_DATASET_KEY);
  } catch {
    return null;
  }
}

function rememberDataset(id: string): void {
  try {
    window.localStorage.setItem(LAST_DATASET_KEY, id);
  } catch {
    // Best effort; storage may be unavailable.
  }
}

export function AddToDatasetDialog({ source, onClose }: AddToDatasetDialogProps) {
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const idPrefix = useId();
  const datasets = useEvalDatasets();
  const custom = (datasets.data?.datasets ?? []).filter((dataset) => isCustomDataset(dataset.source) && dataset.id);
  const [chosen, setChosen] = useState<string | null>(null);
  const [newName, setNewName] = useState('');
  const [draft, setDraft] = useState<QuestionDraft>({
    question: source.question,
    referenceAnswer: source.answer ?? '',
    facts: '',
    sources: [],
    tags: '',
    answerable: true,
    history: [],
  });

  // Feedback has the rated answer but not the question.
  const lookup = useConversation(!source.question && source.conversationId && source.messageId ? source.conversationId : null);
  const [filledFrom, setFilledFrom] = useState<typeof lookup.data>(undefined);
  if (lookup.data && lookup.data !== filledFrom) {
    setFilledFrom(lookup.data);
    const messages = lookup.data.messages;
    const answer = messages.find((message) => message.id === source.messageId);
    const question = answer ? [...messages].reverse().find((message) => message.sequence < answer.sequence && isQuestion(message)) : undefined;
    if (question) {
      setDraft((current) => (current.question ? current : { ...current, question: question.text, referenceAnswer: current.referenceAnswer || answer!.text }));
    }
  }

  const remembered = lastDataset();
  const fallback = custom.find((dataset) => dataset.id === remembered)?.id ?? custom[0]?.id ?? NEW;
  const target = chosen ?? (datasets.data ? fallback : null);
  const creating = target === NEW;

  const save = useMutation({
    mutationFn: async () => {
      let datasetId = target;
      let datasetName = custom.find((dataset) => dataset.id === target)?.name ?? '';
      if (creating) {
        const created = await evalsApi.createDataset({ name: newName.trim() });
        datasetId = created.id ?? null;
        datasetName = created.name;
        // A retry adds to this dataset instead of creating another one.
        setChosen(datasetId);
        void queryClient.invalidateQueries({ queryKey: evalKeys.datasets });
      }
      if (!datasetId) {
        throw new Error('The dataset has no id.');
      }
      await evalsApi.addItem(datasetId, toRequest(draft, { conversationId: source.conversationId ?? null, messageId: source.messageId ?? null }));
      return { id: datasetId, name: datasetName };
    },
    onSuccess: ({ id, name }) => {
      rememberDataset(id);
      void queryClient.invalidateQueries({ queryKey: evalKeys.datasets });
      toast({
        title: `Added to ${name}`,
        description: 'The next run of the dataset asks this question.',
        tone: 'success',
        action: { label: 'Open', onClick: () => navigate(datasetPath(id)) },
      });
      onClose();
    },
  });

  const error = save.error;
  const nameErrors = creating && error instanceof ApiError ? error.errorsFor('name') : [];
  const otherErrors = error instanceof ApiError ? error.errorsExcept([...QUESTION_FIELDS, 'name']) : [];
  const formId = `${idPrefix}-form`;

  const submit = (event: FormEvent) => {
    event.preventDefault();
    save.mutate();
  };

  return (
    <Modal
      open
      onClose={onClose}
      size="lg"
      title="Add to a dataset"
      description="Evaluations ask it again after you change sources or prompts, and check the answer."
      dismissible={!save.isPending}
      footer={
        <>
          <Button onClick={onClose} disabled={save.isPending}>
            Cancel
          </Button>
          <Button type="submit" form={formId} variant="primary" icon={ClipboardPlus} loading={save.isPending} disabled={target === null}>
            Add question
          </Button>
        </>
      }
    >
      <form id={formId} onSubmit={submit} noValidate className="space-y-4">
        {datasets.isPending ? (
          <Spinner label="Loading datasets" />
        ) : datasets.isError ? (
          <ErrorMessage error={datasets.error} title="Could not load the datasets" />
        ) : (
          <div className="grid gap-4 sm:grid-cols-2">
            <Field label="Dataset" htmlFor={`${idPrefix}-dataset`}>
              <Select
                id={`${idPrefix}-dataset`}
                value={target ?? NEW}
                onChange={setChosen}
                options={[...custom.map((dataset) => ({ value: dataset.id!, label: dataset.name })), { value: NEW, label: 'New dataset…' }]}
              />
            </Field>
            {creating ? (
              <Field label="Name of the new dataset" htmlFor={`${idPrefix}-name`} errors={nameErrors}>
                <input
                  {...fieldAria(`${idPrefix}-name`, nameErrors)}
                  autoComplete="off"
                  value={newName}
                  onChange={(event) => setNewName(event.target.value)}
                  placeholder="Questions from visitors"
                  className="field"
                />
              </Field>
            ) : null}
          </div>
        )}

        <QuestionFields idPrefix={idPrefix} draft={draft} onChange={setDraft} error={error} />

        {error && !(error instanceof ApiError && error.isValidation) ? <ErrorMessage error={error} title="The question was not added" /> : null}
        {otherErrors.length > 0 ? (
          <Alert tone="danger" title="The question was not added" live>
            {otherErrors.join(' ')}
          </Alert>
        ) : null}
      </form>
    </Modal>
  );
}
