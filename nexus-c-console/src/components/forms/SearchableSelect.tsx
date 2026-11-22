import { useMemo, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { referenceDataApi } from '../../services/api/modules/platform.api';
import { queryKeys } from '../../services/api/query-keys';

export interface SearchableOption {
  code: string;
  label: string;
  sublabel?: string;
}

interface SearchableSelectProps {
  id: string;
  label: string;
  value: string;
  onChange: (code: string) => void;
  source: 'country' | 'timezone' | 'locale' | 'language' | 'status' | 'currency';
  placeholder?: string;
  disabled?: boolean;
}

/**
 * Accessible searchable selector backed live by NEXUS C Reference Data & ISO Currency APIs.
 * Zero hardcoded world catalogs in frontend bundles.
 */
export function SearchableSelect({
  id,
  label,
  value,
  onChange,
  source,
  placeholder = 'Tìm mã hoặc tên...',
  disabled = false,
}: SearchableSelectProps) {
  const [search, setSearch] = useState('');

  const refQuery = useQuery({
    queryKey: source === 'currency' ? queryKeys.currencies() : queryKeys.referenceData(source),
    queryFn: async ({ signal }): Promise<SearchableOption[]> => {
      if (source === 'currency') {
        const list = await referenceDataApi.listCurrencies(undefined, signal);
        return list.map((c) => ({
          code: c.code,
          label: `${c.code} — ${c.name} (${c.symbol})`,
          sublabel: `Precision ${c.minorUnitPrecision}`,
        }));
      }
      const list = await referenceDataApi.listByCategory(source, undefined, signal);
      return list.map((item) => ({
        code: item.code,
        label: `${item.code} — ${item.label}`,
      }));
    },
    staleTime: 300_000,
    retry: false,
  });

  const filtered = useMemo(() => {
    const items = refQuery.data ?? [];
    const term = search.trim().toLowerCase();
    if (!term) return items;
    return items.filter(
      (opt) =>
        opt.code.toLowerCase().includes(term) ||
        opt.label.toLowerCase().includes(term),
    );
  }, [refQuery.data, search]);

  return (
    <div className="form-field">
      <label className="form-label" htmlFor={id}>
        {label}
      </label>
      <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
        <input
          type="search"
          className="input"
          placeholder={placeholder}
          aria-label={`Lọc ${label}`}
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          disabled={disabled}
        />
        <select
          id={id}
          className="select"
          value={value}
          onChange={(e) => onChange(e.target.value)}
          disabled={disabled || refQuery.isLoading}
        >
          <option value="">
            {refQuery.isLoading ? 'Đang tải danh mục...' : '-- Chọn --'}
          </option>
          {value && !filtered.some((o) => o.code === value) && (
            <option value={value}>{value}</option>
          )}
          {filtered.map((opt) => (
            <option key={opt.code} value={opt.code}>
              {opt.label}
            </option>
          ))}
        </select>
      </div>
    </div>
  );
}
