import { listFleets, listPipelines, listVessels } from '@dashboard/api/client';
import type { Fleet, Pipeline, Vessel } from '@dashboard/types/models';

/** Everything the fleet screens show: fleets, every vessel (for counts and the fleet's vessel list), pipelines. */
export interface FleetData {
  fleets: Fleet[];
  vessels: Vessel[];
  pipelines: Pipeline[];
}

/** Load fleets, vessels, and pipelines in full, as the dashboard's Fleets and Fleet Detail pages do. */
export async function loadFleetData(): Promise<FleetData> {
  const [f, v, p] = await Promise.all([listFleets({ pageSize: 9999 }), listVessels({ pageSize: 9999 }), listPipelines({ pageSize: 9999 })]);
  return { fleets: f?.objects ?? [], vessels: v?.objects ?? [], pipelines: p?.objects ?? [] };
}

/** Vessels of one fleet. */
export function vesselsOfFleet(vessels: Vessel[], fleetId: string): Vessel[] {
  return vessels.filter((v) => v.fleetId === fleetId);
}

/** Vessel count per fleet id. */
export function vesselCounts(vessels: Vessel[]): Map<string, number> {
  const counts = new Map<string, number>();
  for (const v of vessels) if (v.fleetId) counts.set(v.fleetId, (counts.get(v.fleetId) ?? 0) + 1);
  return counts;
}

/** Fleets matching a search term (name or description), sorted by name like the dashboard's default sort. */
export function filterFleets(fleets: Fleet[], term: string): Fleet[] {
  const q = term.trim().toLowerCase();
  return fleets
    .filter((f) => !q || f.name.toLowerCase().includes(q) || (f.description ?? '').toLowerCase().includes(q))
    .sort((a, b) => a.name.toLowerCase().localeCompare(b.name.toLowerCase()));
}
