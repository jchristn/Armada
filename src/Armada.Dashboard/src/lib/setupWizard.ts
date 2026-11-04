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
  fleetSelect: 'The existing fleet the setup vessel joins. A fleet is a named group of related repositories that share defaults and can be targeted together.',
  fleetName: 'Name of the new fleet, a group for related repositories, for example "Web Apps". You can rename it later on the Fleets page.',
  fleetDescription: 'Optional note on what belongs in this fleet. Shown in fleet lists only; captains do not read it.',
  vesselSelect: 'The registered repository (vessel) the first mission runs against. The default branch is shown in parentheses.',
  vesselName: 'Name for this repository inside Armada, shown in lists and used by captains, for example "web-portal". It does not have to match the repository name.',
  defaultBranch: 'Branch that mission branches are created from and landed back into. Blank uses main.',
  repoUrl: 'Where to clone the repository from: a clone URL such as https://github.com/org/repo.git or git@github.com:org/repo.git, or a local repository path. The Admiral keeps its own bare clone of it.',
  workingDirectory: 'Optional path to your own checkout on the Admiral host, for example /home/me/code/repo. Local Merge lands work here, and readiness checks and the workspace read it. Armada never deletes it.',
  landingMode: 'What happens to finished work. Default follows the server setting; None leaves it on a branch for review; Local Merge merges into the working directory; Merge and Push also pushes; Pull Request opens a PR; Merge Queue queues it.',
  enableModelContext: 'When on, captains save what they learn about the repository (build quirks, layout, test patterns) onto the vessel, and later missions receive it in their prompt.',
  projectContext: 'Optional notes added to every captain prompt for this vessel: what the project is, its architecture, key files, and how to build and test it.',
  styleGuide: 'Optional coding rules added to every captain prompt for this vessel: naming, patterns to use or avoid, and preferred libraries.',
  captainSelect: 'An idle captain to take the first mission. Only captains that are not working right now are listed; runtime and state are shown in parentheses.',
  captainName: 'Name for this captain (an AI coding agent), shown in lists and logs, for example "claude-1".',
  runtime: 'Which coding agent CLI this captain runs, such as Claude Code or Codex. That CLI must be installed and signed in on the machine that runs missions.',
  model: 'Optional model name passed to the runtime, for example sonnet for Claude Code or gpt-5.4-mini for Codex. Blank uses the runtime\'s own default model.',
  systemInstructions: 'Optional instructions added to the prompt of every mission this captain runs, on any vessel. Blank adds nothing.',
  muxConfigDirectory: 'Optional folder holding the Mux configuration and saved endpoints, passed as --config-dir. Blank uses MUX_CONFIG_DIR, then ~/.mux.',
  muxEndpoint: 'Name of a saved Mux endpoint (the model server Mux calls), as listed by Mux, for example "local-ollama". Required when the runtime is Mux.',
  missionTitle: 'Short name for the first mission, shown in mission lists and on the branch Armada creates for it.',
  missionDescription: 'The full instructions the captain receives. The default asks for a read-only survey of the repository, so no files change.',
  priority: 'Scheduling order when work is queued: lower numbers run first. 100 is the normal default; 0 to 1000 allowed.',
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
export function normalizeMissionResponse(value: unknown, waitingWarning?: string): { mission: Mission | null; warning?: string } {
  // Servers before v1.3.0 wrapped a mission no captain could take yet as { Mission, Warning }; current servers return
  // the Mission itself, left Pending.
  const maybeWrapped = value as { mission?: Mission; warning?: string } | null;
  if (maybeWrapped?.mission?.id) {
    return { mission: maybeWrapped.mission, warning: waitingWarning ?? maybeWrapped.warning };
  }

  const maybeMission = value as Mission | null;
  if (!maybeMission?.id) return { mission: null };
  return maybeMission.status === 'Pending' && waitingWarning ? { mission: maybeMission, warning: waitingWarning } : { mission: maybeMission };
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
