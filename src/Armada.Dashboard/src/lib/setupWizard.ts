import type { Mission, WorkflowProfile } from '../types/models';

/**
 * Pure parts of the setup wizard (components/SetupWizard.tsx), shared with the mobile app: the steps, field help,
 * mission settling, response normalization, and the handoff links.
 */

export interface WizardStep {
  title: string;
  summary: string;
}

export const SETUP_STEPS: WizardStep[] = [
  { title: 'Objective', summary: 'Configure Armada to dispatch one safe first mission.' },
  { title: 'Fleet', summary: 'Create or choose the group that owns your repository.' },
  { title: 'Vessel', summary: 'Register the git repository that captains will work in.' },
  { title: 'Captain', summary: 'Create or choose an AI runtime so dispatch has capacity.' },
  { title: 'Dispatch', summary: 'Send a low-risk onboarding mission directly to Armada.' },
  { title: 'Handoff', summary: 'Refresh the mission and continue into onboarding, backlog, planning, and delivery setup.' },
];

/** English help text of the wizard's fields (translated where shown). */
export const SETUP_TOOLTIPS = {
  fleetSelect: 'Choose an existing fleet to group this setup vessel under.',
  fleetName: 'Name for the fleet that will organize one or more related repositories.',
  fleetDescription: 'Optional notes describing what repositories belong in this fleet.',
  vesselSelect: 'Choose the existing git repository Armada should dispatch the setup mission to.',
  vesselName: 'Display name for this repository inside Armada.',
  defaultBranch: 'Default branch Armada should branch from when creating mission worktrees.',
  repoUrl: 'Git clone URL or local repository path for the repository Armada will manage.',
  workingDirectory: 'Optional path to your local checkout, used for local landing and git status checks.',
  landingMode: 'Controls how completed mission work is landed. None keeps the work on a branch for you to review; Local Merge merges it into the working directory without pushing; Merge and Push also pushes it to that checkout\'s origin remote.',
  enableModelContext: 'Allow captains to save useful repository knowledge back onto the vessel for future missions.',
  allowConcurrentMissions: 'Allow more than one mission to run on this vessel at the same time.',
  projectContext: 'Optional architecture, build, test, and dependency notes injected into captain prompts.',
  styleGuide: 'Optional coding conventions, naming rules, and library preferences for captains.',
  captainSelect: 'Choose an idle captain that is currently available for mission assignment.',
  captainName: 'Display name for the AI agent runtime registered with Armada.',
  runtime: 'AI agent runtime Armada should launch for missions assigned to this captain.',
  model: 'Optional runtime-specific model override. Leave blank to use the runtime default.',
  systemInstructions: 'Optional instructions injected into every mission handled by this captain.',
  muxConfigDirectory: 'Optional mux config directory override for loading saved endpoints.',
  muxEndpoint: 'Required named mux endpoint when the captain runtime is Mux.',
  missionTitle: 'Short title for the direct setup mission created by dispatch.',
  missionDescription: 'Full task instructions sent to the captain for this setup dispatch.',
  priority: 'Scheduling priority for the mission. Lower values are higher priority in Armada.',
};

/** Mission statuses at which the handoff step stops polling. */
export const SETTLED_MISSION_STATUSES = new Set(['Complete', 'Failed', 'Cancelled', 'WorkProduced', 'LandingFailed', 'PullRequestOpen']);

/** Replaces the item with the same id, or prepends it. */
export function upsertById<T extends { id: string }>(items: T[], item: T): T[] {
  const found = items.some((existing) => existing.id === item.id);
  if (found) return items.map((existing) => (existing.id === item.id ? item : existing));
  return [item, ...items];
}

/** The mission (and any warning) from a dispatch response, which may wrap it as { mission, warning }. */
export function normalizeMissionResponse(value: unknown): { mission: Mission | null; warning?: string } {
  const maybeWrapped = value as { mission?: Mission; warning?: string } | null;
  if (maybeWrapped?.mission?.id) {
    return { mission: maybeWrapped.mission, warning: maybeWrapped.warning };
  }

  const maybeMission = value as Mission | null;
  return maybeMission?.id ? { mission: maybeMission } : { mission: null };
}

/** The first 12 characters of an id, or '-'. */
export function idShort(id?: string | null): string {
  return id ? id.slice(0, 12) : '-';
}

/** Backlog link of the handoff step, filtered to the fleet and vessel when known. */
export function setupBacklogPath(fleetId: string, vesselId: string): string {
  const parts: string[] = [];
  if (fleetId) parts.push(`fleetId=${encodeURIComponent(fleetId)}`);
  if (vesselId) parts.push(`vesselId=${encodeURIComponent(vesselId)}`);
  return parts.length > 0 ? `/backlog?${parts.join('&')}` : '/backlog';
}

/** Workflow profiles that apply to the setup vessel: global, its fleet's, and its own. */
export function relevantWorkflowProfiles(profiles: WorkflowProfile[], fleetId: string, vesselId: string): WorkflowProfile[] {
  return profiles.filter((profile) => {
    if (profile.scope === 'Global') return true;
    if (profile.scope === 'Fleet' && fleetId) return profile.fleetId === fleetId;
    if (profile.scope === 'Vessel') return profile.vesselId === vesselId;
    return false;
  });
}
