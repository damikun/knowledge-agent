import { embedSnippet } from '../widgetForm';
import { CopyBlock } from '../../../components/ui/CopyBlock';
import { ContactAttributesEmbed } from './ContactAttributesEmbed';
import { PublicAddressHint, usePublicOrigin } from '../../system/publicAddress';
import { IdentityVerification, type IdentityState } from './IdentityVerification';

export function EmbedSnippet({ publicKey, identity }: { publicKey: string; identity: IdentityState }) {
  const { origin, placeholder } = usePublicOrigin();
  return (
    <div className="space-y-4">
      <CopyBlock code={embedSnippet(origin, publicKey)} />
      {placeholder ? <PublicAddressHint /> : null}
      <IdentityVerification publicKey={publicKey} identity={identity} />
      <ContactAttributesEmbed publicKey={publicKey} identityEnabled={identity.enabled} />
    </div>
  );
}
