import { useMemo } from 'react';
import { listFleets, listVessels } from '@dashboard/api/client';
import type { Fleet, Vessel } from '@dashboard/types/models';
import type { SelectOption } from '../components/ui/SelectSheet';
import { useLoad } from './useLoad';

/** Every record a list call returns (the dashboard's `pageSize: 9999` reference-data loads). */
export const ALL = { pageSize: 9999 } as const;

/** A reference list for pickers and name lookups; failures leave it empty (the screen still works). */
export function useReference<T>(loader: () => Promise<{ objects?: T[] | null } | null | undefined>, live: readonly string[] = []): T[] {
  const { data } = useLoad(async () => {
    try {
      const result = await loader();
      return result?.objects ?? [];
    } catch {
      return [] as T[];
    }
  }, [], { live });
  return data ?? [];
}

export function useVessels(): Vessel[] {
  return useReference<Vessel>(() => listVessels(ALL));
}

export function useFleets(): Fleet[] {
  return useReference<Fleet>(() => listFleets(ALL));
}

/** id -> name map for showing related records by name. */
export function useNameMap<T extends { id: string; name: string }>(items: T[]): Map<string, string> {
  return useMemo(() => new Map(items.map((item) => [item.id, item.name])), [items]);
}

/** Picker options for records with a name, optionally led by a "none" option. */
export function recordOptions<T extends { id: string; name: string }>(items: T[], none?: string): SelectOption<string>[] {
  const options = items.map((item) => ({ value: item.id, label: item.name }));
  return none !== undefined ? [{ value: '', label: none }, ...options] : options;
}

/** Picker options for string values that are their own label (enum values). */
export function valueOptions(values: readonly string[], label: (value: string) => string = (v) => v): SelectOption<string>[] {
  return values.map((value) => ({ value, label: label(value) }));
}
