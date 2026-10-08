import { useMemo } from 'react';
import { listBacklog, listCaptains, listFleets, listPipelines, listVessels } from '@dashboard/api/client';
import type { Captain, Fleet, Objective, Pipeline, Vessel } from '@dashboard/types/models';
import { sortByName } from '@dashboard/lib/sortByName';
import { useLiveResource } from '../../build/useLiveResource';

/** Everything the backlog pages pick from: fleets, vessels, captains, pipelines, and the other backlog items. */
export interface BacklogReference {
  fleets: Fleet[];
  vessels: Vessel[];
  captains: Captain[];
  pipelines: Pipeline[];
  objectives: Objective[];
  fleetNames: Map<string, string>;
  vesselNames: Map<string, string>;
  captainNames: Map<string, string>;
  error: string | null;
}

const ALL = { pageSize: 9999 };

/**
 * Loads the backlog reference data once per screen (the dashboard's ObjectiveDetail does the same five calls) and
 * keeps captain states current (captain.changed) so the refinement captain picker shows who is idle.
 */
export function useBacklogReference(options: { captains?: boolean; pipelines?: boolean; objectives?: boolean } = {}): BacklogReference {
  const { captains = true, pipelines = true, objectives = true } = options;
  const resource = useLiveResource(async () => {
    const [fleetResult, vesselResult, captainResult, pipelineResult, objectiveResult] = await Promise.all([
      listFleets(ALL),
      listVessels(ALL),
      captains ? listCaptains(ALL) : Promise.resolve(null),
      pipelines ? listPipelines(ALL) : Promise.resolve(null),
      objectives ? listBacklog(ALL) : Promise.resolve(null),
    ]);
    return {
      fleets: fleetResult?.objects ?? [],
      vessels: sortByName(vesselResult?.objects ?? []),
      captains: captainResult?.objects ?? [],
      pipelines: pipelineResult?.objects ?? [],
      objectives: objectiveResult?.objects ?? [],
    };
  }, [captains, pipelines, objectives], { live: captains ? ['captain.'] : undefined });

  return useMemo(() => {
    const data = resource.data ?? { fleets: [], vessels: [], captains: [], pipelines: [], objectives: [] };
    return {
      ...data,
      fleetNames: new Map(data.fleets.map((f) => [f.id, f.name])),
      vesselNames: new Map(data.vessels.map((v) => [v.id, v.name])),
      captainNames: new Map(data.captains.map((c) => [c.id, c.name])),
      error: resource.error,
    };
  }, [resource.data, resource.error]);
}
