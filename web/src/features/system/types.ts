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

export type PublicAddressSource = 'saved' | 'configuration' | 'email';

export interface PublicAddressDto {
  url: string | null;
  source: PublicAddressSource | null;
}

export type JobEngine = 'inngest' | 'inprocess';

export interface JobScheduleDto {
  name: string;
  kind: string;
  cron: string;
  enabled: boolean;
  nextRunAt: IsoDateTime | null;
}

export interface JobsOverviewDto {
  engine: JobEngine;
  dashboardUrl: string | null;
  schedules: JobScheduleDto[];
}

export type ComponentStatus = 'healthy' | 'degraded' | 'down' | 'notConfigured' | 'unknown';

type SystemComponentKey = 'api' | 'database' | 'vectorStore' | 'jobs' | 'signIn' | 'langfuse' | 'models' | 'email' | 'slack';

export interface SystemComponentHealthDto {
  key: SystemComponentKey | (string & {});
  status: ComponentStatus;
  detail: string;
  checkedAt: IsoDateTime;
  variant?: string | null;
}

export interface SystemHealthDto {
  components: SystemComponentHealthDto[];
}
