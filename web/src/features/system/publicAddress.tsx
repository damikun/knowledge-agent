import { useMutation, useQueryClient } from '@tanstack/react-query';
import { Link } from 'react-router';
import { isRecord, request } from '../../lib/api';
import { PLACEHOLDER_ORIGIN, publicOrigin } from '../../lib/publicOrigin';
import { systemInfoQueryKey } from '../../lib/queryKeys';
import { useReturnState } from '../../lib/returnTo';
import { useSystemInfo } from './api';
import type { PublicAddressDto, PublicAddressSource, SystemInfoDto } from './types';

const PUBLIC_ADDRESS_PATH = '/system#public-address';

const SOURCES: readonly PublicAddressSource[] = ['saved', 'configuration', 'email'];

export function toPublicAddress(value: unknown): PublicAddressDto {
  const dto = isRecord(value) ? value : {};
  const source = SOURCES.find((item) => item === dto.source) ?? null;
  return { url: typeof dto.url === 'string' && dto.url !== '' ? dto.url : null, source };
}

/** The public address once known, else the portal's own origin, else a placeholder while running on localhost. */
export function usePublicOrigin(): { origin: string; placeholder: boolean } {
  const info = useSystemInfo();
  const origin = publicOrigin(toPublicAddress(info.data?.publicAddress).url);
  return { origin, placeholder: origin === PLACEHOLDER_ORIGIN };
}

export function useSavePublicAddress() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (url: string | null) =>
      toPublicAddress(await request<unknown>('/api/system/public-address', { method: 'PUT', body: { url } })),
    onSuccess: (publicAddress) => {
      queryClient.setQueryData<SystemInfoDto>(systemInfoQueryKey, (info) => (info ? { ...info, publicAddress } : info));
    },
  });
}

export function PublicAddressHint() {
  const returnState = useReturnState();
  return (
    <p className="text-xs text-fg-muted">
      The code shows yourdomain.com because no public address is set.{' '}
      <Link to={PUBLIC_ADDRESS_PATH} state={returnState} className="font-medium text-accent-text underline-offset-2 hover:underline">
        Set your public address
      </Link>
    </p>
  );
}
