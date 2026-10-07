import { useCallback, useEffect, useMemo, useState } from 'react';
import { listCaptains, listFleets, listVessels } from '@dashboard/api/client';
import type { Captain, Fleet, Vessel } from '@dashboard/types/models';

export interface NameLookups {
  vessels: Vessel[];
  captains: Captain[];
  fleets: Fleet[];
  vesselName: (id: string | null | undefined) => string;
  captainName: (id: string | null | undefined) => string;
  fleetName: (id: string | null | undefined) => string;
  reload: () => void;
}

/** Pure: an entity's name by id, the first 8 characters of the id when unknown, '-' when there is no id. */
export function nameFrom<T extends { id: string; name: string }>(items: readonly T[], id: string | null | undefined): string {
  if (!id) return '-';
  const found = items.find((item) => item.id === id);
  return found ? found.name : id.slice(0, 8);
}

/**
 * Vessel, captain, and fleet names for lists that show ids (the dashboard pages load the same lists with a large
 * page size). Each list is best-effort: a failed load leaves ids shown.
 */
export function useNameLookups(which: { vessels?: boolean; captains?: boolean; fleets?: boolean } = { vessels: true, captains: true }): NameLookups {
  const [vessels, setVessels] = useState<Vessel[]>([]);
  const [captains, setCaptains] = useState<Captain[]>([]);
  const [fleets, setFleets] = useState<Fleet[]>([]);
  const [version, setVersion] = useState(0);
  const wantVessels = !!which.vessels;
  const wantCaptains = !!which.captains;
  const wantFleets = !!which.fleets;

  useEffect(() => {
    let cancelled = false;
    if (wantVessels) listVessels({ pageSize: 9999 }).then((r) => { if (!cancelled) setVessels(r?.objects ?? []); }).catch(() => undefined);
    if (wantCaptains) listCaptains({ pageSize: 9999 }).then((r) => { if (!cancelled) setCaptains(r?.objects ?? []); }).catch(() => undefined);
    if (wantFleets) listFleets({ pageSize: 9999 }).then((r) => { if (!cancelled) setFleets(r?.objects ?? []); }).catch(() => undefined);
    return () => { cancelled = true; };
  }, [wantVessels, wantCaptains, wantFleets, version]);

  const vesselName = useCallback((id: string | null | undefined) => nameFrom(vessels, id), [vessels]);
  const captainName = useCallback((id: string | null | undefined) => nameFrom(captains, id), [captains]);
  const fleetName = useCallback((id: string | null | undefined) => nameFrom(fleets, id), [fleets]);
  const reload = useCallback(() => setVersion((v) => v + 1), []);
  return useMemo(() => ({ vessels, captains, fleets, vesselName, captainName, fleetName, reload }), [vessels, captains, fleets, vesselName, captainName, fleetName, reload]);
}
