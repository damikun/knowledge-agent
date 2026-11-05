import { useMutation, useQueryClient } from '@tanstack/react-query';
import { KeyRound, RefreshCw } from 'lucide-react';
import { useId, useState } from 'react';
import { Badge } from '../../../components/ui/Badge';
import { Button } from '../../../components/ui/Button';
import { Alert, ErrorMessage } from '../../../components/ui/feedback';
import { ConfirmDialog } from '../../../components/ui/Modal';
import { formatDateTime } from '../../../lib/format';
import type { Guid } from '../../../lib/types';
import { widgetKeys, widgetsApi } from '../api';
import type { WidgetDto } from '../types';
import { identifySnippet } from '../widgetForm';
import { usePublicOrigin } from '../../system/publicAddress';
import { CopyBlock } from '../../../components/ui/CopyBlock';

export interface IdentityState {
  widgetId: Guid;
  enabled: boolean;
  createdAt: string | null;
}

type Confirming = 'rotate' | 'off' | null;

export function IdentityVerification({ publicKey, identity }: { publicKey: string; identity: IdentityState }) {
  const queryClient = useQueryClient();
  const headingId = useId();
  const [secret, setSecret] = useState<string | null>(null);
  const [confirming, setConfirming] = useState<Confirming>(null);
  const { origin } = usePublicOrigin();

  const patchWidget = (patch: Partial<WidgetDto>) =>
    queryClient.setQueryData<WidgetDto[]>(widgetKeys.list, (widgets) => widgets?.map((widget) => (widget.id === identity.widgetId ? { ...widget, ...patch } : widget)));

  const create = useMutation({
    mutationFn: () => widgetsApi.createIdentitySecret(identity.widgetId),
    onSuccess: (created) => {
      setSecret(created.secret);
      setConfirming(null);
      patchWidget({ identityVerification: true, identitySecretCreatedAt: created.createdAt });
    },
  });
  const remove = useMutation({
    mutationFn: () => widgetsApi.removeIdentitySecret(identity.widgetId),
    onSuccess: (widget) => {
      setSecret(null);
      setConfirming(null);
      patchWidget({ identityVerification: false, identitySecretCreatedAt: null, settings: widget.settings });
    },
  });

  return (
    <section aria-labelledby={headingId} className="space-y-3 border-t border-border-subtle pt-4">
      <div className="flex flex-wrap items-center gap-2">
        {/* Not a heading: the disclosure above holds the section's heading level. */}
        <p id={headingId} className="text-sm font-medium text-fg">
          Signed-in users
        </p>
        <Badge tone={identity.enabled ? 'success' : 'neutral'} dot>
          {identity.enabled ? 'Identity verification on' : 'Off'}
        </Badge>
      </div>
      <p className="text-xs text-fg-muted">
        Tell the chat who is signed in on your website. Their conversations then show under one person in People, on every device. Your server
        signs each user id with a secret, so a page cannot pretend to be someone else.
      </p>

      {secret ? (
        <Alert tone="warning" title="Copy the secret now">
          <p className="mb-2">It is not shown again. Keep it on your server only, never in the page.</p>
          <CopyBlock code={secret} label="Copy secret" copiedText="The secret is on your clipboard." />
        </Alert>
      ) : null}

      {identity.enabled ? (
        <>
          <p className="text-xs text-fg-muted">
            {identity.createdAt ? `Secret created ${formatDateTime(identity.createdAt)}. ` : ''}
            Compute USER_HASH on your server as HMAC-SHA256 of the user id with the secret, in hex.
          </p>
          <CopyBlock code={identifySnippet(origin, publicKey)} />
          <div className="flex flex-wrap gap-2">
            <Button size="sm" icon={RefreshCw} onClick={() => setConfirming('rotate')}>
              Create new secret
            </Button>
            <Button size="sm" variant="danger-ghost" onClick={() => setConfirming('off')}>
              Turn off
            </Button>
          </div>
        </>
      ) : (
        <Button size="sm" icon={KeyRound} loading={create.isPending} onClick={() => create.mutate()}>
          Turn on identity verification
        </Button>
      )}
      {!confirming && create.error ? <ErrorMessage error={create.error} title="The secret could not be created" /> : null}

      <ConfirmDialog
        open={confirming === 'rotate'}
        title="Create a new secret?"
        message="The current secret stops working at once. Signed-in users chat as visitors until your server uses the new one."
        confirmLabel="Create new secret"
        tone="primary"
        busy={create.isPending}
        error={create.error ? <ErrorMessage error={create.error} /> : null}
        onConfirm={() => create.mutate()}
        onCancel={() => setConfirming(null)}
      />
      <ConfirmDialog
        open={confirming === 'off'}
        title="Turn off identity verification?"
        message="The chat stops trusting the users your website names, and anyone can chat again. People and their conversations stay."
        confirmLabel="Turn off"
        busy={remove.isPending}
        error={remove.error ? <ErrorMessage error={remove.error} /> : null}
        onConfirm={() => remove.mutate()}
        onCancel={() => setConfirming(null)}
      />
    </section>
  );
}
