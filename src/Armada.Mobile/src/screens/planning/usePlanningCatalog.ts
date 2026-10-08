import { useMemo } from 'react';
import { listCaptains, listFleets, listPipelines, listVessels } from '@dashboard/api/client';
import type { Captain, Fleet, Pipeline, Vessel } from '@dashboard/types/models';
import { sortByName } from '@dashboard/lib/sortByName';
import { useLiveResource } from '../../build/useLiveResource';

/** The lists the Planning screens resolve names from and start sessions with (the dashboard's loadCatalog). */
export interface PlanningCatalog {
  captains: Captain[];
  fleets: Fleet[];
  vessels: Vessel[];
  pipelines: Pipeline[];
  loading: boolean;
  captainName: (id: string) => string;
  vesselName: (id: string) => string;
  pipelineName: (id: string | null) => string;
}

const ALL = { pageSize: 9999 };

/**
 * Captains (live: reloaded on captain.changed so readiness to plan stays current), fleets, vessels (by name), and
 * pipelines. A list that fails to load is treated as empty, like the dashboard.
 */
export function usePlanningCatalog(): PlanningCatalog {
  const captains = useLiveResource(async () => (await listCaptains(ALL).catch(() => null))?.objects ?? [], [], { live: ['captain.changed', 'captain.created', 'captain.deleted'] });
  const rest = useLiveResource(async () => {
    const [fleets, vessels, pipelines] = await Promise.all([
      listFleets(ALL).catch(() => null),
      listVessels(ALL).catch(() => null),
      listPipelines(ALL).catch(() => null),
    ]);
    return {
      fleets: fleets?.objects ?? [],
      vessels: sortByName(vessels?.objects),
      pipelines: pipelines?.objects ?? [],
    };
  }, []);

  return useMemo(() => {
    const captainList = captains.data ?? [];
    const fleets = rest.data?.fleets ?? [];
    const vessels = rest.data?.vessels ?? [];
    const pipelines = rest.data?.pipelines ?? [];
    const captainNames = new Map(captainList.map((c) => [c.id, c.name]));
    const vesselNames = new Map(vessels.map((v) => [v.id, v.name]));
    const pipelineNames = new Map(pipelines.map((p) => [p.id, p.name]));
    return {
      captains: captainList,
      fleets,
      vessels,
      pipelines,
      loading: captains.loading || rest.loading,
      captainName: (id: string) => captainNames.get(id) || id,
      vesselName: (id: string) => vesselNames.get(id) || id,
      pipelineName: (id: string | null) => (id && pipelineNames.get(id)) || id || '-',
    };
  }, [captains.data, captains.loading, rest.data, rest.loading]);
}
