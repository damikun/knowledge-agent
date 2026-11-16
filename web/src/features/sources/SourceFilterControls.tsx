import { FilterX } from 'lucide-react';
import { Button } from '../../components/ui/Button';
import { FilterChips, type FilterChipOption } from '../../components/ui/FilterChips';
import { Select } from '../../components/ui/Select';
import { formatNumber } from '../../lib/format';
import { TYPE_FILTERS, type StatusFilter, type TypeFilter } from './sourceFilters';
import type { SourceSiteDto } from './types';

export interface StatusFiltersProps {
  options: readonly FilterChipOption<StatusFilter>[];
  value: StatusFilter;
  onChange: (value: StatusFilter) => void;
  onClear?: () => void;
}

export function StatusFilters({ options, value, onChange, onClear }: StatusFiltersProps) {
  return (
    <>
      <FilterChips label="Status" options={options} value={value} onChange={onChange} />
      {onClear ? (
        <Button variant="ghost" size="sm" icon={FilterX} onClick={onClear}>
          Clear
        </Button>
      ) : null}
    </>
  );
}

export interface TypeAndSiteFiltersProps {
  type: TypeFilter;
  onType: (value: TypeFilter) => void;
  site: string;
  onSite: (value: string) => void;
  sites: readonly SourceSiteDto[];
}

export function TypeAndSiteFilters({ type, onType, site, onSite, sites }: TypeAndSiteFiltersProps) {
  const siteOptions = [
    { value: '', label: 'All sites' },
    ...(site && !sites.some((option) => option.site === site) ? [{ value: site, label: site }] : []),
    ...sites.map((option) => ({ value: option.site, label: option.site, hint: formatNumber(option.count) })),
  ];
  return (
    <div className="flex items-center gap-2">
      <Select aria-label="Type" size="sm" options={TYPE_FILTERS} value={type} onChange={onType} className="w-32" />
      {sites.length > 0 || site ? <Select aria-label="Site" size="sm" options={siteOptions} value={site} onChange={onSite} align="end" className="w-36" /> : null}
    </div>
  );
}
