import { KeyRound, PlugZap, WifiOff } from 'lucide-react';
import { Link } from 'react-router';
import { Banner } from '../../components/ui/Banner';
import { useProfiles } from '../models/api';
import { PROVIDERS_PATH } from '../models/providerSettings';
import { useSystemInfo } from './api';
import { isApiUnreachable, setupMessage } from './status';
import { useReturnState } from '../../lib/returnTo';

/** At most one banner, for the most blocking problem: an unreachable API, then a missing model. */
export function SystemBanners() {
  const returnState = useReturnState();
  const info = useSystemInfo();
  const profiles = useProfiles();

  if (isApiUnreachable(info.error) && isApiUnreachable(profiles.error)) {
    return (
      <Banner
        tone="danger"
        icon={WifiOff}
        action={
          <button
            type="button"
            onClick={() => {
              void info.refetch();
              void profiles.refetch();
            }}
          >
            Retry
          </button>
        }
      >
        Cannot reach the API. Data shown may be out of date.
      </Banner>
    );
  }

  const setup = setupMessage(profiles.data);
  if (setup) {
    return (
      <Banner
        tone="warning"
        icon={PlugZap}
        action={
          <Link to={PROVIDERS_PATH} state={returnState}>
            <KeyRound className="size-3.5 shrink-0" aria-hidden />
            Add an API key
          </Link>
        }
      >
        {setup}
      </Banner>
    );
  }

  return null;
}
