/*
 * Every kind of Armada record the native resource views cover, grouped by sidebar view: Backlog, Fleet Actions,
 * Delivery (releases, deployments, environments, incidents, runbooks, checks), Configuration (personas, pipelines,
 * playbooks, prompt templates, workflow and project profiles, skills, model endpoints), Activity (events, signals) and
 * System (Harbors, docks, CLI permission rules, tenants, users, credentials, battery packs). Calls go through the
 * shared dashboard client.
 */

import * as vscode from 'vscode';
import * as api from '@dashboard/api/client';
import type { Credential, Harbor } from '@dashboard/types/models';
import { ResourceRecord, ResourceSpec, rowsOf } from './resourceSpec';
import { buildHistoryTimelineQuery } from '@dashboard/lib/historyExport';

/** Interactive helpers the catalog's actions need. */
export interface CatalogDeps {
  /** Multi-select vessel picker; undefined when cancelled. */
  pickVessels(title: string): Promise<string[] | undefined>;
  /** Single captain picker; undefined when cancelled. */
  pickCaptain(title: string): Promise<string | undefined>;
  /** Single vessel picker; undefined when cancelled. */
  pickVessel(title: string): Promise<string | undefined>;
  /** Opens a planning session's chat. */
  openPlanning(sessionId: string): Promise<void>;
  /** Opens a refinement session's chat. */
  openRefinement(sessionId: string): Promise<void>;
}

const page = { pageNumber: 1, pageSize: 1000 };
const recent = { pageNumber: 1, pageSize: 200 };
const str = (r: ResourceRecord, k: string): string => (typeof r[k] === 'string' ? (r[k] as string) : '');
const id = (r: ResourceRecord): string => String(r.id ?? '');
const asRecord = (value: unknown): ResourceRecord => (value && typeof value === 'object' ? (value as ResourceRecord) : {});
const join = (...parts: Array<string | false | null | undefined>): string => parts.filter(Boolean).join(' · ');

async function comment(title: string): Promise<string | undefined> {
  return vscode.window.showInputBox({ title, prompt: 'Comment (optional)', ignoreFocusOut: true });
}

/** The catalog. */
export function resourceCatalog(deps: CatalogDeps): ResourceSpec[] {
  return [
    // Fleet and captains: listed in the Fleet and Captains views, edited here.
    {
      key: 'vessel', label: 'Vessel', plural: 'Vessels', icon: 'repo', view: 'configuration', hidden: true,
      list: async () => rowsOf(await api.listVessels(page)),
      get: async (i) => asRecord(await api.getVessel(i)),
      create: (d) => api.createVessel(d as never),
      update: (i, d) => api.updateVessel(i, d as never),
      remove: (i) => api.deleteVessel(i),
      readOnly: ['hasGitHubTokenOverride', 'gitHubTokenOverride'],
      createFields: [
        { key: 'name', label: 'Name', type: 'text', required: true },
        { key: 'repoUrl', label: 'Repository URL', type: 'text', required: true, placeholder: 'https://github.com/org/repo.git' },
        { key: 'defaultBranch', label: 'Default branch', type: 'text', value: 'main', required: true },
        { key: 'localPath', label: 'Local path', type: 'text', hint: 'Optional: an existing clone on the Admiral host.' },
      ],
      actions: [
        { label: 'Show Git Status', showResult: 'Git status', run: (r) => api.getVesselGitStatus(id(r)) },
        { label: 'Show Recent Commits', showResult: 'Recent commits', run: (r) => api.getVesselCommits(id(r), { pageSize: 50 } as never) },
        { label: 'Show Readiness', showResult: 'Readiness', run: (r) => api.getVesselReadiness(id(r)) },
        { label: 'Show Health', showResult: 'Health', run: (r) => api.getVesselHealth(id(r)) },
        { label: 'Show Concurrency', showResult: 'Concurrency', run: (r) => api.getVesselConcurrency(id(r)) },
        { label: 'Show Reservations', showResult: 'Reservations', run: (r) => api.listVesselReservations(id(r), { pageNumber: 1, pageSize: 200 }) },
      ],
      route: (r) => `/vessels/${id(r)}`,
    },
    {
      key: 'fleet', label: 'Fleet', plural: 'Fleets', icon: 'folder-library', view: 'configuration', hidden: true,
      list: async () => rowsOf(await api.listFleets(page)),
      get: async (i) => asRecord(await api.getFleet(i)),
      create: (d) => api.createFleet(d as never),
      update: (i, d) => api.updateFleet(i, d as never),
      remove: (i) => api.deleteFleet(i),
      createFields: [
        { key: 'name', label: 'Name', type: 'text', required: true },
        { key: 'description', label: 'Description', type: 'textarea' },
      ],
      route: (r) => `/fleets/${id(r)}`,
    },
    {
      key: 'captain', label: 'Captain', plural: 'Captains', icon: 'person', view: 'configuration', hidden: true,
      list: async () => rowsOf(await api.listCaptains(page)),
      get: async (i) => asRecord(await api.getCaptain(i)),
      create: (d) => api.createCaptain(d as never),
      update: (i, d) => api.updateCaptain(i, d as never),
      remove: (i) => api.deleteCaptain(i),
      readOnly: ['state', 'processId', 'recoveryAttempts', 'lastHeartbeatUtc', 'activeMissionIds', 'activeMissionCount', 'currentSessionId', 'usageCaptureActive', 'supportsPlanningSessions', 'planningSessionSupportReason'],
      createFields: [
        { key: 'name', label: 'Name', type: 'text', required: true },
        { key: 'runtime', label: 'Runtime', type: 'select', value: 'ClaudeCode', options: ['ClaudeCode', 'Codex', 'Gemini', 'Cursor', 'Mux', 'OpenCode', 'Pi', 'ApiEndpoint'].map((v) => ({ value: v, label: v })) },
        { key: 'model', label: 'Model', type: 'text', hint: 'Optional; the runtime default when empty.' },
        { key: 'systemInstructions', label: 'System instructions', type: 'textarea' },
      ],
      actions: [{
        label: 'Evaluate (Quick)',
        confirm: (r) => `Run a quick evaluation of "${str(r, 'name')}"? It sends a few probe prompts to the model.`,
        run: async (r) => {
          const run = await api.evaluateCaptain(id(r), 'Quick');
          void vscode.window.showInformationMessage(`Armada: evaluation of "${str(r, 'name')}" started (see Activity, Evaluations).`);
          return run;
        },
      }],
      route: (r) => `/captains/${id(r)}`,
    },

    // Backlog
    {
      key: 'backlog', label: 'Backlog Item', plural: 'Backlog', icon: 'lightbulb', view: 'backlog',
      list: async () => rowsOf(await api.listBacklog({ pageNumber: 1, pageSize: 500 } as never)),
      get: async (i) => asRecord(await api.getBacklogItem(i)),
      create: (d) => api.createBacklogItem(d as never),
      update: (i, d) => api.updateBacklogItem(i, d as never),
      remove: (i) => api.deleteBacklogItem(i),
      describe: (r) => join(str(r, 'status'), str(r, 'priority'), str(r, 'kind')),
      createFields: [
        { key: 'title', label: 'Title', type: 'text', required: true },
        { key: 'description', label: 'Description', type: 'textarea' },
        { key: 'acceptanceCriteria', label: 'Acceptance criteria', type: 'textarea', kind: 'lines', hint: 'One per line.' },
      ],
      actions: [
        {
          label: 'Refine with a Captain...',
          run: async (r) => {
            const captainId = await deps.pickCaptain(`Refine "${str(r, 'title')}" with`);
            if (!captainId) return undefined;
            const session = await api.createObjectiveRefinementSession(id(r), { captainId, title: str(r, 'title') });
            await deps.openRefinement(session.session.id);
            return session;
          },
        },
        {
          label: 'Plan with a Captain...',
          run: async (r) => {
            const captainId = await deps.pickCaptain(`Plan "${str(r, 'title')}" with`);
            if (!captainId) return undefined;
            const vesselId = await deps.pickVessel('Plan on vessel');
            if (!vesselId) return undefined;
            const session = await api.createPlanningSession({ captainId, vesselId, title: str(r, 'title'), objectiveId: id(r) });
            await deps.openPlanning(session.session.id);
            return session;
          },
        },
      ],
      route: (r) => `/backlog/${id(r)}`,
    },
    {
      key: 'planningSession', label: 'Planning Session', plural: 'Planning Sessions', icon: 'comment-discussion', view: 'backlog',
      list: async () => rowsOf(await api.listPlanningSessions({ pageNumber: 1, pageSize: 200 } as never)),
      remove: (i) => api.deletePlanningSession(i),
      describe: (r) => join(str(r, 'status'), str(r, 'createdUtc').slice(0, 10)),
      open: (r) => deps.openPlanning(id(r)),
      createCommand: 'armada.planning.new',
      route: (r) => `/dispatch?tab=planning&session=${id(r)}`,
    },

    // Fleet Actions
    {
      key: 'fleetAction', label: 'Fleet Action', plural: 'Actions', icon: 'run-all', view: 'fleetActions',
      list: async () => rowsOf(await api.enumerateFleetActions({ pageNumber: 1, pageSize: 500 } as never)),
      get: async (i) => asRecord(await api.getFleetAction(i)),
      update: (i, d) => api.updateFleetAction(i, d as never),
      remove: (i) => api.deleteFleetAction(i),
      readOnly: ['isBuiltIn', 'builtInKey'],
      describe: (r) => join(str(r, 'kind'), r.isBuiltIn ? 'built in' : ''),
      actions: [{
        label: 'Run on Vessels...',
        run: async (r) => {
          const vesselIds = await deps.pickVessels(`Run "${str(r, 'name')}" on`);
          if (!vesselIds?.length) return undefined;
          const result = await api.runFleetAction(id(r), { VesselIds: vesselIds });
          void vscode.window.showInformationMessage(`Armada: started "${str(r, 'name')}" on ${vesselIds.length} ${vesselIds.length === 1 ? 'vessel' : 'vessels'}.`);
          return result;
        },
      }],
      route: (r) => `/fleet-actions/${id(r)}`,
    },
    {
      key: 'fleetActionRun', label: 'Fleet Action Run', plural: 'Runs', icon: 'play-circle', view: 'fleetActions',
      list: async () => rowsOf(await api.enumerateFleetActionRuns({ pageNumber: 1, pageSize: 100 } as never)),
      get: async (i) => asRecord(await api.getFleetActionRun(i)),
      describe: (r) => join(str(r, 'status'), str(r, 'actionName')),
      actions: [{
        label: 'Cancel Run',
        when: (r) => ['Running', 'Pending', 'Queued'].includes(str(r, 'status')),
        confirm: () => 'Cancel this fleet action run?',
        run: (r) => api.cancelFleetActionRun(id(r)),
      }],
      route: (r) => `/fleet-actions/runs/${id(r)}`,
    },

    // Delivery
    {
      key: 'release', label: 'Release', plural: 'Releases', icon: 'tag', view: 'delivery',
      list: async () => rowsOf(await api.listReleases(recent as never)),
      get: async (i) => asRecord(await api.getRelease(i)),
      create: (d) => api.createRelease(d as never),
      update: (i, d) => api.updateRelease(i, d as never),
      remove: (i) => api.deleteRelease(i),
      describe: (r) => join(str(r, 'version'), str(r, 'status')),
      createFields: [
        { key: 'title', label: 'Title', type: 'text', required: true },
        { key: 'version', label: 'Version', type: 'text' },
        { key: 'summary', label: 'Summary', type: 'textarea' },
      ],
      actions: [{ label: 'Refresh Release', run: (r) => api.refreshRelease(id(r)) }],
      route: (r) => `/releases/${id(r)}`,
    },
    {
      key: 'deployment', label: 'Deployment', plural: 'Deployments', icon: 'cloud-upload', view: 'delivery',
      list: async () => rowsOf(await api.listDeployments(recent as never)),
      get: async (i) => asRecord(await api.getDeployment(i)),
      remove: (i) => api.deleteDeployment(i),
      describe: (r) => join(str(r, 'status'), str(r, 'environmentName')),
      actions: [
        { label: 'Approve', when: (r) => /pending|approval/i.test(str(r, 'status')), run: async (r) => { const c = await comment('Approve deployment'); return c === undefined ? undefined : api.approveDeployment(id(r), c || null); } },
        { label: 'Deny', when: (r) => /pending|approval/i.test(str(r, 'status')), run: async (r) => { const c = await comment('Deny deployment'); return c === undefined ? undefined : api.denyDeployment(id(r), c || null); } },
        { label: 'Verify', run: (r) => api.verifyDeployment(id(r)) },
        { label: 'Roll Back', confirm: (r) => `Roll back "${str(r, 'title') || id(r)}"?`, run: (r) => api.rollbackDeployment(id(r)) },
      ],
      route: (r) => `/deployments/${id(r)}`,
    },
    {
      key: 'environment', label: 'Environment', plural: 'Environments', icon: 'server-environment', view: 'delivery',
      list: async () => rowsOf(await api.listEnvironments(page as never)),
      get: async (i) => asRecord(await api.getEnvironment(i)),
      create: (d) => api.createEnvironment(d as never),
      update: (i, d) => api.updateEnvironment(i, d as never),
      remove: (i) => api.deleteEnvironment(i),
      describe: (r) => join(str(r, 'kind'), r.isDefault ? 'default' : '', r.requiresApproval ? 'approval required' : ''),
      createFields: [
        { key: 'name', label: 'Name', type: 'text', required: true },
        { key: 'description', label: 'Description', type: 'textarea' },
        { key: 'baseUrl', label: 'Base URL', type: 'text' },
      ],
      route: (r) => `/environments/${id(r)}`,
    },
    {
      key: 'incident', label: 'Incident', plural: 'Incidents', icon: 'flame', view: 'delivery',
      list: async () => rowsOf(await api.listIncidents(recent as never)),
      get: async (i) => asRecord(await api.getIncident(i)),
      create: (d) => api.createIncident(d as never),
      update: (i, d) => api.updateIncident(i, d as never),
      remove: (i) => api.deleteIncident(i),
      describe: (r) => join(str(r, 'severity'), str(r, 'status'), str(r, 'environmentName')),
      createFields: [
        { key: 'title', label: 'Title', type: 'text', required: true },
        { key: 'severity', label: 'Severity', type: 'select', value: 'Medium', options: ['Low', 'Medium', 'High', 'Critical'].map((v) => ({ value: v, label: v })) },
        { key: 'summary', label: 'Summary', type: 'textarea' },
      ],
      route: (r) => `/incidents/${id(r)}`,
    },
    {
      key: 'runbook', label: 'Runbook', plural: 'Runbooks', icon: 'notebook', view: 'delivery',
      list: async () => rowsOf(await api.listRunbooks(page as never)),
      get: async (i) => asRecord(await api.getRunbook(i)),
      update: (i, d) => api.updateRunbook(i, d as never),
      remove: (i) => api.deleteRunbook(i),
      describe: (r) => join(str(r, 'environmentName'), str(r, 'fileName')),
      actions: [{
        label: 'Start Execution',
        confirm: (r) => `Start the runbook "${str(r, 'title')}"?`,
        run: (r) => api.startRunbookExecution(id(r), {} as never),
      }],
      route: (r) => `/runbooks/${id(r)}`,
    },
    {
      key: 'checkRun', label: 'Check Run', plural: 'Checks', icon: 'beaker', view: 'delivery',
      list: async () => rowsOf(await api.listCheckRuns(recent)),
      get: async (i) => asRecord(await api.getCheckRun(i)),
      remove: (i) => api.deleteCheckRun(i),
      describe: (r) => join(str(r, 'status'), str(r, 'type'), str(r, 'branchName')),
      actions: [{ label: 'Retry', run: (r) => api.retryCheckRun(id(r)) }],
      route: (r) => `/checks/${id(r)}`,
    },

    // Configuration
    {
      key: 'persona', label: 'Persona', plural: 'Personas', icon: 'account', view: 'configuration', idKey: 'name',
      list: async () => rowsOf(await api.listPersonas(page)),
      get: async (i) => asRecord(await api.getPersona(i)),
      create: (d) => api.createPersona(d as never),
      update: (i, d) => api.updatePersona(i, d as never),
      remove: (i) => api.deletePersona(i),
      readOnly: ['isBuiltIn', 'name'],
      describe: (r) => join(str(r, 'promptTemplateName'), r.isBuiltIn ? 'built in' : ''),
      createFields: [
        { key: 'name', label: 'Name', type: 'text', required: true },
        { key: 'description', label: 'Description', type: 'textarea' },
        { key: 'promptTemplateName', label: 'Prompt template', type: 'text', required: true, hint: 'The name of the prompt template the persona uses.' },
      ],
      route: (r) => `/personas/${encodeURIComponent(str(r, 'name'))}`,
    },
    {
      key: 'pipeline', label: 'Pipeline', plural: 'Pipelines', icon: 'type-hierarchy', view: 'configuration',
      list: async () => rowsOf(await api.listPipelines(page)),
      get: async (i) => asRecord(await api.getPipeline(i)),
      create: (d) => api.createPipeline(d as never),
      update: (i, d) => api.updatePipeline(i, d as never),
      remove: (i) => api.deletePipeline(i),
      readOnly: ['isBuiltIn'],
      describe: (r) => join(Array.isArray(r.stages) ? `${(r.stages as unknown[]).length} stages` : '', r.isBuiltIn ? 'built in' : ''),
      createFields: [
        { key: 'name', label: 'Name', type: 'text', required: true },
        { key: 'description', label: 'Description', type: 'textarea' },
        { key: 'stages', label: 'Stages', type: 'textarea', kind: 'json', value: '[\n  { "personaName": "Worker", "order": 1 }\n]', hint: 'JSON array of stages.' },
      ],
      route: (r) => `/pipelines/${id(r)}`,
    },
    {
      key: 'playbook', label: 'Playbook', plural: 'Playbooks', icon: 'book', view: 'configuration',
      list: async () => rowsOf(await api.listPlaybooks(page)),
      get: async (i) => asRecord(await api.getPlaybook(i)),
      create: (d) => api.createPlaybook(d as never),
      update: (i, d) => api.updatePlaybook(i, d as never),
      remove: (i) => api.deletePlaybook(i),
      describe: (r) => str(r, 'description'),
      createFields: [
        { key: 'fileName', label: 'File name', type: 'text', required: true, placeholder: 'release-checklist.md' },
        { key: 'description', label: 'Description', type: 'text' },
        { key: 'content', label: 'Content', type: 'textarea', required: true },
      ],
      route: (r) => `/playbooks/${id(r)}`,
    },
    {
      key: 'promptTemplate', label: 'Prompt Template', plural: 'Prompt Templates', icon: 'symbol-text', view: 'configuration', idKey: 'name',
      list: async () => rowsOf(await api.listPromptTemplates(page)),
      get: async (i) => asRecord(await api.getPromptTemplate(i)),
      create: (d) => api.createPromptTemplate(d as never),
      update: (i, d) => api.updatePromptTemplate(i, { content: String(d.content ?? ''), description: (d.description as string | undefined) ?? undefined }),
      readOnly: ['isBuiltIn', 'name', 'category', 'scope', 'active'],
      describe: (r) => join(str(r, 'category'), r.isBuiltIn ? 'built in' : ''),
      createFields: [
        { key: 'name', label: 'Name', type: 'text', required: true },
        { key: 'category', label: 'Category', type: 'text', required: true, value: 'persona' },
        { key: 'description', label: 'Description', type: 'text' },
        { key: 'content', label: 'Content', type: 'textarea', required: true },
      ],
      actions: [{ label: 'Reset to Default', when: (r) => r.isBuiltIn === true, confirm: (r) => `Reset "${str(r, 'name')}" to its built-in content?`, run: (r) => api.resetPromptTemplate(str(r, 'name')) }],
      route: (r) => `/prompt-templates/${encodeURIComponent(str(r, 'name'))}`,
    },
    {
      key: 'workflowProfile', label: 'Workflow Profile', plural: 'Workflow Profiles', icon: 'workflow', view: 'configuration',
      list: async () => rowsOf(await api.listWorkflowProfiles(page)),
      get: async (i) => asRecord(await api.getWorkflowProfile(i)),
      create: (d) => api.createWorkflowProfile(d as never),
      update: (i, d) => api.updateWorkflowProfile(i, d as never),
      remove: (i) => api.deleteWorkflowProfile(i),
      describe: (r) => join(str(r, 'scope'), r.isDefault ? 'default' : ''),
      createFields: [
        { key: 'name', label: 'Name', type: 'text', required: true },
        { key: 'description', label: 'Description', type: 'textarea' },
        { key: 'buildCommand', label: 'Build command', type: 'text' },
        { key: 'unitTestCommand', label: 'Unit test command', type: 'text' },
      ],
      route: (r) => `/workflow-profiles/${id(r)}`,
    },
    {
      key: 'projectProfile', label: 'Project Profile', plural: 'Project Profiles', icon: 'project', view: 'configuration',
      list: async () => rowsOf(await api.listProjectProfiles(page)),
      get: async (i) => asRecord(await api.getProjectProfile(i)),
      create: (d) => api.createProjectProfile(d as never),
      update: (i, d) => api.updateProjectProfile(i, d as never),
      remove: (i) => api.deleteProjectProfile(i),
      describe: (r) => join(str(r, 'scope'), r.isDefault ? 'default' : ''),
      createFields: [
        { key: 'name', label: 'Name', type: 'text', required: true },
        { key: 'description', label: 'Description', type: 'textarea' },
      ],
      route: (r) => `/project-profiles/${id(r)}`,
    },
    {
      key: 'skill', label: 'Skill', plural: 'Skills', icon: 'tools', view: 'configuration',
      list: async () => rowsOf(await api.listSkills(page)),
      get: async (i) => asRecord(await api.getSkill(i)),
      create: (d) => api.createSkill(d as never),
      update: (i, d) => api.updateSkill(i, d as never),
      remove: (i) => api.deleteSkill(i),
      readOnly: ['isBuiltIn'],
      describe: (r) => join(str(r, 'category'), r.isBuiltIn ? 'built in' : ''),
      createFields: [
        { key: 'name', label: 'Name', type: 'text', required: true },
        { key: 'category', label: 'Category', type: 'text' },
        { key: 'description', label: 'Description', type: 'text' },
        { key: 'content', label: 'Content', type: 'textarea', required: true },
      ],
      route: (r) => `/skills/${id(r)}`,
    },
    {
      key: 'modelEndpoint', label: 'Model Endpoint', plural: 'Model Endpoints', icon: 'hubot', view: 'configuration',
      list: async () => rowsOf(await api.listModelEndpoints()),
      get: async (i) => asRecord(await api.getModelEndpoint(i)),
      update: (i, d) => api.updateModelEndpoint(i, d as never),
      remove: (i) => api.deleteModelEndpoint(i),
      readOnly: ['hasApiKey', 'healthStatus'],
      describe: (r) => join(str(r, 'provider'), str(r, 'model'), str(r, 'healthStatus')),
      actions: [{ label: 'Check Health of All Endpoints', run: () => api.healthCheckModelEndpoints() }],
      route: (r) => `/model-endpoints/${id(r)}`,
    },

    {
      key: 'memory', label: 'Memory', plural: 'Memories', icon: 'database', view: 'configuration',
      list: async () => rowsOf(await api.listMemories({ pageNumber: 1, pageSize: 500 })),
      get: async (i) => asRecord(await api.getMemory(i)),
      create: (d) => api.createMemory(d as never),
      update: (i, d) => api.updateMemory(i, d as never),
      remove: (i) => api.deleteMemory(i),
      describe: (r) => join(str(r, 'scope'), str(r, 'category')),
      createFields: [
        { key: 'title', label: 'Title', type: 'text', required: true },
        { key: 'content', label: 'Content', type: 'textarea', required: true },
      ],
      route: () => '/configuration?tab=memory',
    },

    {
      key: 'failureCauseRule', label: 'Failure Cause Rule', plural: 'Failure Cause Rules', icon: 'law', view: 'configuration',
      list: async () => rowsOf(await api.listFailureCauseRules(page)),
      get: async (i) => asRecord(await api.getFailureCauseRule(i)),
      create: (d) => api.createFailureCauseRule(d as never),
      update: (i, d) => api.updateFailureCauseRule(i, d as never),
      remove: (i) => api.deleteFailureCauseRule(i),
      readOnly: ['isBuiltIn'],
      describe: (r) => join(`priority ${String(r.priority ?? '')}`, r.enabled === false ? 'disabled' : '', r.isBuiltIn ? 'built in' : ''),
      createFields: [
        { key: 'name', label: 'Name', type: 'text', required: true },
        { key: 'description', label: 'Description', type: 'text' },
        { key: 'priority', label: 'Priority', type: 'number', value: '100', kind: 'number' },
        { key: 'match', label: 'Match', type: 'textarea', kind: 'json', value: '{\n  "messageContains": ""\n}', hint: 'JSON: what a failure must look like for the rule to apply.' },
        { key: 'cause', label: 'Cause', type: 'textarea', kind: 'json', value: '{\n  "category": "Environment",\n  "summary": ""\n}', hint: 'JSON: the cause the rule assigns.' },
      ],
      route: () => '/configuration',
    },

    // Activity
    {
      key: 'history', label: 'Activity', plural: 'All Activity', icon: 'timeline-open', view: 'activity',
      // The dashboard History tab's default query: newest first, without GET/HEAD/OPTIONS request rows (a large
      // timeline with every read request does not answer in time).
      list: async () => rowsOf(await api.enumerateHistoryTimeline({ ...buildHistoryTimelineQuery({ objectiveId: '', text: '', actor: '', vesselId: '', sourceType: 'all', postmortemOnly: false, showReadRequests: false }, 100, 1), order: 'CreatedDescending' })),
      describe: (r) => join(str(r, 'entityType') || str(r, 'sourceType'), str(r, 'occurredUtc').replace('T', ' ').slice(0, 19)),
      route: () => '/activity?source=history',
    },
    {
      key: 'stageOutcome', label: 'Stage Outcome', plural: 'Stage Outcomes', icon: 'milestone', view: 'activity',
      list: async () => rowsOf(await api.listStageOutcomes({ pageNumber: 1, pageSize: 200 } as never)),
      get: async (i) => asRecord(await api.getStageOutcome(i)),
      describe: (r) => join(str(r, 'outcome'), str(r, 'causeCategory') || str(r, 'category'), str(r, 'missionId')),
      actions: [{ label: 'Reclassify All Outcomes', confirm: () => 'Reclassify every stage outcome with the current failure cause rules?', showResult: 'Reclassification', run: () => api.reclassifyStageOutcomes({}) }],
      route: () => '/activity',
    },
    {
      key: 'job', label: 'Background Job', plural: 'Background Jobs', icon: 'gear', view: 'activity',
      list: async () => rowsOf(await api.listJobs({ pageNumber: 1, pageSize: 200 })),
      get: async (i) => asRecord(await api.getJob(i)),
      describe: (r) => join(str(r, 'status'), str(r, 'kind')),
      actions: [{ label: 'Cancel Job', when: (r) => ['Queued', 'Running', 'Pending'].includes(str(r, 'status')), confirm: () => 'Cancel this background job?', run: (r) => api.cancelJob(id(r)) }],
      route: () => '/activity?source=events',
    },
    {
      key: 'event', label: 'Event', plural: 'Events', icon: 'pulse', view: 'activity',
      list: async () => rowsOf(await api.listEvents({ pageNumber: 1, pageSize: 200 })),
      get: async (i) => asRecord(await api.getEvent(i)),
      describe: (r) => join(str(r, 'eventType'), str(r, 'createdUtc').replace('T', ' ').slice(0, 19)),
      route: (r) => `/events/${id(r)}`,
    },
    {
      key: 'signal', label: 'Signal', plural: 'Signals', icon: 'broadcast', view: 'activity',
      list: async () => rowsOf(await api.listSignals({ pageNumber: 1, pageSize: 200 })),
      get: async (i) => asRecord(await api.getSignal(i)),
      describe: (r) => join(str(r, 'type'), r.read === false ? 'unread' : '', str(r, 'createdUtc').replace('T', ' ').slice(0, 19)),
      actions: [{ label: 'Mark Read', when: (r) => r.read === false, run: (r) => api.markSignalRead(id(r)) }],
      route: (r) => `/signals/${id(r)}`,
    },

    {
      key: 'requestHistory', label: 'Request', plural: 'Request History', icon: 'history', view: 'activity',
      list: async () => rowsOf(await api.listRequestHistory({ pageNumber: 1, pageSize: 200 })),
      get: async (i) => asRecord(await api.getRequestHistoryEntry(i)),
      describe: (r) => join(`${String(r.statusCode ?? '')}`, `${String(r.durationMs ?? '')} ms`, str(r, 'principalDisplay')),
      route: () => '/activity?source=requests',
    },
    {
      key: 'evaluation', label: 'Evaluation', plural: 'Evaluations', icon: 'beaker', view: 'activity',
      list: async () => rowsOf(await api.listModelProbeRuns({ pageNumber: 1, pageSize: 100 })),
      get: async (i) => asRecord(await api.getModelProbeRun(i, true)),
      describe: (r) => join(str(r, 'status'), str(r, 'captainName') || str(r, 'model'), str(r, 'depth')),
      actions: [{ label: 'Cancel Evaluation', when: (r) => ['Queued', 'Running'].includes(str(r, 'status')), confirm: () => 'Cancel this evaluation?', run: (r) => api.cancelModelProbeRun(id(r)) }],
      route: (r) => `/evaluations/${id(r)}`,
    },
    {
      key: 'vesselHealth', label: 'Vessel Health', plural: 'Vessel Health', icon: 'heart', view: 'activity', idKey: 'vesselId',
      list: async () => rowsOf(await api.enumerateVesselHealth({ PageNumber: 1, PageSize: 1000 })).map((r) => ({ name: r.vesselName ?? r.vesselId, ...r })),
      get: async (i) => asRecord(await api.getVesselHealth(i)),
      describe: (r) => join(str(r, 'overallStatus'), str(r, 'currentBranch'), r.isDirty ? 'dirty' : ''),
      actions: [{ label: 'Evaluate All Vessels', run: () => api.evaluateVesselHealth({}) }],
      route: (r) => `/vessels/${str(r, 'vesselId')}`,
    },
    {
      key: 'tokenUsage', label: 'Model Usage', plural: 'Token Usage (7 days)', icon: 'graph', view: 'activity', idKey: 'model',
      list: async () => {
        const usage = await api.getTokenUsage({ fromUtc: new Date(Date.now() - 7 * 86400000).toISOString(), modelLimit: 50 });
        return (usage.byModel ?? []).map((m) => ({ ...m, name: m.model }));
      },
      describe: (r) => `${Number(r.totalTokens ?? 0).toLocaleString()} tokens`,
      route: () => '/activity?source=tokens',
    },
    {
      key: 'captainUsage', label: 'Captain Usage', plural: 'Captain Usage', icon: 'dashboard', view: 'activity',
      list: async () => rowsOf(await api.getUsageLatest({})).map((r, i) => ({ ...r, id: r.id ?? `usage-${i}`, name: [r.runtime, r.provider, r.accountKey].filter(Boolean).join(' · ') })),
      describe: (r) => join(str(r, 'status'), r.stale ? 'stale' : '', str(r, 'capturedUtc').replace('T', ' ').slice(0, 16)),
      route: () => '/activity?source=usage',
    },

    // System
    {
      key: 'server', label: 'Admiral', plural: 'Admiral', icon: 'server', view: 'system',
      list: async () => {
        const [health, status] = await Promise.all([api.getHealth(), api.getStatus().catch(() => ({}))]);
        return [{ id: 'admiral', name: 'Admiral', ...asRecord(health), ...asRecord(status) }];
      },
      actions: [{ label: 'Restart Admiral', confirm: () => 'Restart the Armada Admiral? Running work pauses while it restarts.', run: () => api.restartServer() }],
      route: () => '/server',
    },
    {
      key: 'settings', label: 'Server Settings', plural: 'Server Settings', icon: 'settings-gear', view: 'system',
      list: async () => [{ id: 'settings', name: 'Server Settings', ...asRecord(await api.getSettings()) }],
      update: async (_i, d) => {
        const data: ResourceRecord = { ...d };
        delete data.id;
        delete data.name;
        return api.updateSettings(data as never);
      },
      readOnly: ['usageSelectionCapabilities', 'usageSelectionRevision', 'name'],
      route: () => '/server',
    },
    {
      key: 'diagnostics', label: 'Diagnostic Check', plural: 'Diagnostics', icon: 'checklist', view: 'system', idKey: 'name',
      list: async () => rowsOf(await api.getDoctor()),
      describe: (r) => join(str(r, 'status'), str(r, 'message')),
      route: () => '/server?tab=diagnostics',
    },
    {
      key: 'storage', label: 'Black Box Storage', plural: 'Storage', icon: 'database', view: 'system',
      list: async () => [{ id: 'blackbox-storage', name: 'Black Box Storage', ...asRecord(await api.getBlackBoxStorage()) }],
      route: () => '/server?tab=storage',
    },
    {
      key: 'harbor', label: 'Harbor', plural: 'Harbors', icon: 'vm-connect', view: 'system',
      list: async () => rowsOf(await api.listHarbors()),
      get: async (i) => asRecord(await api.getHarbor(i)),
      update: (i, d) => api.updateHarbor(i, d as never),
      remove: (i) => api.deleteHarbor(i),
      readOnly: ['connectionStatus', 'protocolVersion', 'osPlatform', 'architecture'],
      describe: (r) => join(str(r, 'connectionStatus'), r.enabled === false ? 'disabled' : '', str(r, 'osPlatform')),
      actions: [
        { label: 'Enable', when: (r) => (r as unknown as Harbor).enabled === false, run: (r) => api.enableHarbor(id(r)) },
        { label: 'Disable', when: (r) => (r as unknown as Harbor).enabled !== false, confirm: (r) => `Disable the Harbor "${str(r, 'name')}"? It stops taking jobs.`, run: (r) => api.disableHarbor(id(r)) },
        { label: 'Show Jobs', showResult: 'Harbor jobs', run: (r) => api.listHarborJobs(id(r), { pageNumber: 1, pageSize: 200 }) },
        { label: 'Show Metrics (24 hours)', showResult: 'Harbor metrics', run: (r) => api.getHarborMetrics(id(r), '24h') },
      ],
      route: (r) => `/harbors/${id(r)}`,
    },
    {
      key: 'dock', label: 'Dock', plural: 'Docks', icon: 'archive', view: 'system',
      list: async () => rowsOf(await api.listDocks(page)),
      get: async (i) => asRecord(await api.getDock(i)),
      remove: (i) => api.deleteDock(i),
      describe: (r) => join(str(r, 'branchName'), r.active === false ? 'inactive' : 'active'),
      route: (r) => `/docks/${id(r)}`,
    },
    {
      key: 'cliPermissionRule', label: 'CLI Permission Rule', plural: 'CLI Permission Rules', icon: 'shield', view: 'system',
      list: async () => rowsOf(await api.listCliPermissionRules()),
      get: async (i) => asRecord(await api.getCliPermissionRule(i)),
      update: (i, d) => api.updateCliPermissionRule(i, d as never),
      remove: (i) => api.deleteCliPermissionRule(i),
      describe: (r) => join(str(r, 'decision'), str(r, 'toolName'), str(r, 'scope')),
      route: () => '/server?tab=cli-permissions',
    },
    {
      key: 'tenant', label: 'Tenant', plural: 'Tenants', icon: 'organization', view: 'system',
      list: async () => rowsOf(await api.listTenants(page)),
      create: (d) => api.createTenant(d as never),
      update: (i, d) => api.updateTenant(i, d as never),
      remove: (i) => api.deleteTenant(i),
      describe: (r) => (r.active === false ? 'inactive' : ''),
      createFields: [{ key: 'name', label: 'Name', type: 'text', required: true }],
      route: () => '/server?tab=tenants',
    },
    {
      key: 'user', label: 'User', plural: 'Users', icon: 'person', view: 'system',
      list: async () => rowsOf(await api.listUsers(page)),
      create: (d) => api.createUser(d as never),
      update: (i, d) => api.updateUser(i, d as never),
      remove: (i) => api.deleteUser(i),
      readOnly: ['passwordSha256', 'password'],
      describe: (r) => join(r.isAdmin ? 'admin' : r.isTenantAdmin ? 'tenant admin' : '', r.active === false ? 'inactive' : ''),
      createFields: [
        { key: 'email', label: 'Email', type: 'text', required: true },
        { key: 'password', label: 'Password', type: 'text', required: true },
        { key: 'firstName', label: 'First name', type: 'text' },
        { key: 'lastName', label: 'Last name', type: 'text' },
      ],
      route: () => '/server?tab=users',
    },
    {
      key: 'credential', label: 'Credential', plural: 'Credentials', icon: 'key', view: 'system',
      list: async () => rowsOf(await api.listCredentials(page)),
      create: (d) => api.createCredential(d as never),
      update: (i, d) => api.updateCredential(i, d as never),
      remove: (i) => api.deleteCredential(i),
      readOnly: ['bearerToken', 'isProtected'],
      describe: (r) => join(r.isProtected ? 'protected' : '', r.active === false ? 'inactive' : ''),
      createFields: [{ key: 'name', label: 'Name', type: 'text' }],
      actions: [{
        label: 'Copy Bearer Token',
        run: async (r) => {
          await vscode.env.clipboard.writeText((r as unknown as Credential).bearerToken);
          void vscode.window.showInformationMessage('Armada: bearer token copied.');
        },
      }],
      route: () => '/server?tab=credentials',
    },
    {
      key: 'batteryPack', label: 'Battery Pack', plural: 'Battery Packs', icon: 'package', view: 'system',
      list: async () => rowsOf(await api.listBatteryPacks()),
      get: async (i) => asRecord(await api.getBatteryPack(i)),
      remove: (i) => api.deleteBatteryPack(i),
      describe: (r) => str(r, 'description'),
      route: () => '/captains?tab=battery',
    },
  ];
}
