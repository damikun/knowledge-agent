import { useId, useState } from 'react';
import { SegmentedControl } from '../../../components/ui/SegmentedControl';
import { serverSnippet, updateSnippet, type ServerLanguage } from '../widgetForm';
import { usePublicOrigin } from '../../system/publicAddress';
import { CopyBlock } from '../../../components/ui/CopyBlock';

const LANGUAGES: readonly { value: ServerLanguage; label: string }[] = [
  { value: 'node', label: 'Node.js' },
  { value: 'csharp', label: 'C#' },
  { value: 'python', label: 'Python' },
];

export function ContactAttributesEmbed({ publicKey, identityEnabled }: { publicKey: string; identityEnabled: boolean }) {
  const headingId = useId();
  const [language, setLanguage] = useState<ServerLanguage>('node');
  const { origin } = usePublicOrigin();
  return (
    <section aria-labelledby={headingId} className="space-y-3 border-t border-border-subtle pt-4">
      <p id={headingId} className="text-sm font-medium text-fg">
        Contact attributes
      </p>
      <p className="text-xs text-fg-muted">
        Attributes such as plan or company let you filter contacts and build segments. Send them with identify() for signed-in users, or with update() for
        anyone. Values from update() on an anonymous page are marked as not verified, because a visitor can change them.
      </p>
      <CopyBlock code={updateSnippet()} label="Copy update() snippet" />
      <p className="text-sm font-medium text-fg">From your server</p>
      <p className="text-xs text-fg-muted">
        {identityEnabled
          ? 'The most trusted way: your backend creates or updates a contact by user id. Sign the timestamp, the user id and the body with the identity secret. Values set this way are never overwritten from the browser.'
          : 'Turn on identity verification above first: its secret signs these requests.'}
      </p>
      {identityEnabled ? (
        <>
          <SegmentedControl label="Server language" options={LANGUAGES} value={language} onChange={setLanguage} />
          <CopyBlock code={serverSnippet(origin, publicKey, language)} label="Copy server snippet" copiedText="The code is on your clipboard." />
        </>
      ) : null}
    </section>
  );
}
