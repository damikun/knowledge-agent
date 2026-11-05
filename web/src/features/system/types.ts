import type { IsoDateTime } from '../../lib/types';
import type { ProviderDto } from '../models/types';

export interface SystemInfoDto {
  version: string;
  environment: string;
  persistenceProvider: string;
  vectorStoreProvider: string;
  promptStore: string;
  langfuseConfigured: boolean;
  langfuseUrl: string | null;
  researcherEnabled: boolean;
  providers: ProviderDto[];
  publicAddress: PublicAddressDto;
}

/** saved: Settings > System; configuration; or derived from the email settings. */
export type PublicAddressSource = 'saved' | 'configuration' | 'email';

export interface PublicAddressDto {
  url: string | null;
  source: PublicAddressSource | null;
}

export type JobEngine = 'inngest' | 'inprocess';

export interface JobScheduleDto {
  name: string;
  kind: string;
  /** Evaluated in UTC. */
  cron: string;
  enabled: boolean;
  /** Null while the schedule is disabled. */
  nextRunAt: IsoDateTime | null;
}

export interface JobsOverviewDto {
  engine: JobEngine;
  /** Null when the jobs run in process. */
  dashboardUrl: string | null;
  schedules: JobScheduleDto[];
}

/** Checked on the server and cached briefly. */
export type ComponentStatus = 'healthy' | 'degraded' | 'down' | 'notConfigured' | 'unknown';

type SystemComponentKey = 'api' | 'database' | 'vectorStore' | 'jobs' | 'signIn' | 'langfuse' | 'models' | 'email' | 'slack';

export interface SystemComponentHealthDto {
  key: SystemComponentKey | (string & {});
  status: ComponentStatus;
  /** Plain words; never an address or a secret. */
  detail: string;
  checkedAt: IsoDateTime;
  /** e.g. "inmemory", "postgres", "inngest" or "inprocess". */
  variant?: string | null;
}

export interface SystemHealthDto {
  components: SystemComponentHealthDto[];
}
