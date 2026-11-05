import { Trash2 } from 'lucide-react';
import { useId, useState } from 'react';
import { useNavigate } from 'react-router';
import { Button } from '../../components/ui/Button';
import { Field } from '../../components/ui/Field';
import { ErrorMessage } from '../../components/ui/feedback';
import { Modal } from '../../components/ui/Modal';
import { toast } from '../../components/ui/Toast';
import { formatNumber } from '../../lib/format';
import { useErasePerson } from './api';
import type { Person } from './types';

// Erasure also deletes every conversation and can't be undone, so the name must be typed to confirm.
export function ErasePersonDialog({ person, open, onClose }: { person: Person; open: boolean; onClose: () => void }) {
  const navigate = useNavigate();
  const erase = useErasePerson(person.id);
  const [typed, setTyped] = useState('');
  const inputId = useId();
  const matches = typed.trim() === person.name;

  const close = () => {
    setTyped('');
    erase.reset();
    onClose();
  };

  return (
    <Modal
      open={open}
      onClose={close}
      title={`Erase ${person.name}?`}
      size="sm"
      dismissible={!erase.isPending}
      footer={
        <>
          <Button onClick={close} disabled={erase.isPending}>
            Cancel
          </Button>
          <Button
            variant="danger"
            icon={Trash2}
            disabled={!matches}
            loading={erase.isPending}
            onClick={() =>
              erase.mutate(undefined, {
                onSuccess: () => {
                  toast({ title: `Erased ${person.name}`, tone: 'success' });
                  void navigate('/contacts', { replace: true });
                },
              })
            }
          >
            Erase person
          </Button>
        </>
      }
    >
      <div className="space-y-3 text-sm text-fg-secondary">
        <p>
          This deletes the person, their {formatNumber(person.stats.conversations)} {person.stats.conversations === 1 ? 'conversation' : 'conversations'}, every
          rating and request for a person, and their browsers. It cannot be undone. Export first if you need a copy.
        </p>
        <Field label={`Type ${person.name} to confirm`} htmlFor={inputId}>
          <input id={inputId} className="field" value={typed} autoComplete="off" onChange={(event) => setTyped(event.target.value)} />
        </Field>
        {erase.error ? <ErrorMessage error={erase.error} title="The person could not be erased" /> : null}
      </div>
    </Modal>
  );
}
