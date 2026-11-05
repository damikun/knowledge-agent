import { Link } from 'react-router';
import { pluralize } from '../../lib/format';
import { PROVIDERS_PATH } from '../models/providerSettings';
import { useReturnState } from '../../lib/returnTo';

/** Shared by the Sources page and the empty chat. */
export function WaitingSourcesMessage({ count }: { count: number }) {
  const returnState = useReturnState();
  const one = count === 1;
  return (
    <>
      {pluralize(count, 'source')} {one ? 'waits' : 'wait'} for an embedding model. Connect one on the{' '}
      <Link to={PROVIDERS_PATH} state={returnState} className="font-medium underline underline-offset-2">
        AI models page
      </Link>{' '}
      and {one ? 'it is' : 'they are'} queued automatically.
    </>
  );
}
