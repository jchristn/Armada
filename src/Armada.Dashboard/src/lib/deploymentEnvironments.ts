import type { DeploymentEnvironment } from '../types/models';

/** One Environment picker option. */
export interface EnvironmentOption {
  id: string;
  label: string;
}

/** Builds Environment picker options: names only when a vessel is chosen, otherwise "vessel / environment". */
export function buildEnvironmentOptions(
  environments: DeploymentEnvironment[],
  vesselNames: Map<string, string>,
  vesselChosen: boolean,
  noVesselLabel: string,
): EnvironmentOption[] {
  const options = environments.map((environment) => {
    if (vesselChosen) return { id: environment.id, label: environment.name };
    const vesselName = environment.vesselId ? (vesselNames.get(environment.vesselId) || environment.vesselId) : noVesselLabel;
    return { id: environment.id, label: `${vesselName} / ${environment.name}` };
  });
  return options.sort((a, b) => a.label.localeCompare(b.label, undefined, { sensitivity: 'base' }));
}
