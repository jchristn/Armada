/*
 * The deeper half of the catalog: what a mission, voyage or black box entry can show beyond its page (instructions,
 * landing preview, pull request reviews and checks, captain selection, the voyage tree, nearby successes, ...), the
 * record kinds the dashboard reaches from sub-tabs (runbook executions, failure fingerprints, recovery stats,
 * retention, Mux endpoints, storage hosts), and the create forms the first half leaves out. Hidden kinds are not
 * listed in a view; they are opened from the native mission, voyage and failure surfaces ("More...").
 */

import * as vscode from 'vscode';
import * as api from '@dashboard/api/client';
import { describeError } from '../../transport/errors';
import { showForm } from '../ops/formPanel';
import { ResourceRecord, ResourceSpec, rowsOf } from './resourceSpec';

const str = (r: ResourceRecord, k: string): string => (typeof r[k] === 'string' ? (r[k] as string) : '');
const id = (r: ResourceRecord): string => String(r.id ?? '');
const asRecord = (value: unknown): ResourceRecord => (value && typeof value === 'object' ? (value as ResourceRecord) : {});
/** Check run types (CheckRunType). */
const CHECK_TYPES = ['Lint', 'Build', 'UnitTest', 'AbbreviatedUnitTest', 'IntegrationTest', 'Migration', 'SecurityScan', 'Performance', 'Package', 'DeploymentVerification', 'RollbackVerification', 'PublishArtifact', 'ReleaseVersioning', 'Changelog', 'Deploy', 'Rollback'];
const join = (...parts: Array<string | false | null | undefined>): string => parts.filter(Boolean).join(' · ');

/** The extra kinds. */
export function resourceCatalogExtras(): ResourceSpec[] {
  return [
    // Mission, voyage and black box entry details, reached with "More..." from their native surfaces.
    {
      key: 'mission', label: 'Mission', plural: 'Missions', icon: 'checklist', view: 'activity', hidden: true,
      list: async () => rowsOf(await api.listMissions({ pageNumber: 1, pageSize: 100 })),
      get: async (i) => asRecord(await api.getMission(i)),
      remove: (i) => api.deleteMission(i),
      actions: [
        { label: 'Show Instructions', showResult: 'Instructions', run: (r) => api.getMissionInstructions(id(r)) },
        { label: 'Show Landing Preview', showResult: 'Landing preview', run: (r) => api.getMissionLandingPreview(id(r)) },
        { label: 'Show Pull Request', when: (r) => !!str(r, 'prUrl'), showResult: 'Pull request', run: (r) => api.getMissionGitHubPullRequest(id(r)) },
        { label: 'Show Pull Request Reviews', when: (r) => !!str(r, 'prUrl'), showResult: 'Pull request reviews', run: (r) => api.enumerateMissionPullRequestReviews(id(r), { pageNumber: 1, pageSize: 100 }) },
        { label: 'Show Pull Request Comments', when: (r) => !!str(r, 'prUrl'), showResult: 'Pull request comments', run: (r) => api.enumerateMissionPullRequestComments(id(r), { pageNumber: 1, pageSize: 100 }) },
        { label: 'Show Pull Request Checks', when: (r) => !!str(r, 'prUrl'), showResult: 'Pull request checks', run: (r) => api.enumerateMissionPullRequestChecks(id(r), { pageNumber: 1, pageSize: 100 }) },
        { label: 'Show What It Waits On', showResult: 'Waits', run: (r) => api.getMissionWaits(id(r)) },
        { label: 'Show Captain Selection', showResult: 'Captain selection', run: (r) => api.getCaptainSelection(id(r)) },
        { label: 'Show Captain Selection History', showResult: 'Captain selection history', run: (r) => api.getCaptainSelectionHistory(id(r)) },
        { label: 'Show Black Box Brief', when: (r) => !!str(r, 'lastBlackBoxEntryId') || ['Failed', 'LandingFailed'].includes(str(r, 'status')), showResult: 'Black box brief', run: (r) => api.getMissionBlackBoxBrief(id(r)) },
        { label: 'Check the Brief', showResult: 'Brief lint', run: (r) => api.lintMissionBrief({ vesselId: str(r, 'vesselId'), title: str(r, 'title'), description: str(r, 'description') } as never) },
        {
          label: 'Queue for Landing',
          when: (r) => !!str(r, 'branchName') && ['WorkProduced', 'Complete', 'LandingFailed'].includes(str(r, 'status')),
          confirm: (r) => `Queue ${str(r, 'branchName')} for landing through the merge queue?`,
          run: (r) => api.enqueueMerge({ missionId: id(r), vesselId: str(r, 'vesselId'), branchName: str(r, 'branchName') }),
        },
      ],
      route: (r) => `/missions/${id(r)}`,
    },
    {
      key: 'voyage', label: 'Voyage', plural: 'Voyages', icon: 'rocket', view: 'activity', hidden: true,
      list: async () => rowsOf(await api.listVoyages({ pageNumber: 1, pageSize: 100 })),
      get: async (i) => asRecord((await api.getVoyage(i)).voyage),
      actions: [
        { label: 'Show Mission Tree', showResult: 'Mission tree', run: (r) => api.getVoyageTree(id(r)) },
        { label: 'Show Status', showResult: 'Voyage status', run: (r) => api.getVoyageStatus(id(r)) },
        { label: 'Show Black Box Cascade', showResult: 'Failure cascade', run: (r) => api.getVoyageBlackBoxCascade(id(r)) },
        { label: 'Show Black Box Brief', showResult: 'Black box brief', run: (r) => api.getVoyageBlackBoxBrief(id(r)) },
        { label: 'Show Concurrency', showResult: 'Concurrency', run: async () => api.getConcurrencyMetrics({ from: new Date(Date.now() - 86400000).toISOString() }) },
      ],
      route: (r) => `/voyages/${id(r)}`,
    },
    {
      key: 'blackBoxEntry', label: 'Black Box Entry', plural: 'Black Box Entries', icon: 'debug-alt', view: 'activity', hidden: true,
      list: async () => [],
      get: async (i) => asRecord(await api.getBlackBox(i)),
      remove: (i) => api.deleteBlackBox(i),
      actions: [
        { label: 'Download Bundle...', run: (r) => api.downloadBlackBox(id(r)) },
        { label: 'Show Nearby Successes', showResult: 'Nearby successes', run: (r) => api.listBlackBoxNearbySuccesses(id(r), { pageNumber: 1, pageSize: 50 }) },
        { label: 'Show Captured Objects', showResult: 'Captured objects', run: (r) => api.listBlackBoxObjects(id(r), { pageNumber: 1, pageSize: 200 }) },
        { label: 'Export as Test Fixture', showResult: 'Test fixture', run: (r) => api.exportBlackBoxFixture(id(r)) },
        {
          label: 'Apply a Recommendation...',
          run: async (r) => {
            const recs = (asRecord(r.analysis).recommendations ?? asRecord(r.resilience).recommendations ?? []) as Array<{ id?: string; title?: string; summary?: string }>;
            if (!recs.length) {
              void vscode.window.showInformationMessage('Armada: this entry has no recommendations.');
              return undefined;
            }
            const picked = await vscode.window.showQuickPick(recs.filter((x) => x.id).map((x) => ({ label: x.title ?? x.id ?? '', detail: x.summary, rec: x })), { title: 'Apply a recommendation' });
            if (!picked?.rec.id) return undefined;
            return api.applyBlackBoxRecommendation(id(r), picked.rec.id);
          },
        },
      ],
      route: (r) => `/blackbox/${id(r)}`,
    },

    // Delivery: runbook executions.
    {
      key: 'runbookExecution', label: 'Runbook Execution', plural: 'Runbook Executions', icon: 'debug-start', view: 'delivery',
      list: async () => rowsOf(await api.listRunbookExecutions({ pageNumber: 1, pageSize: 200 } as never)),
      get: async (i) => asRecord(await api.getRunbookExecution(i)),
      update: (i, d) => api.updateRunbookExecution(i, d as never),
      remove: (i) => api.deleteRunbookExecution(i),
      describe: (r) => join(str(r, 'status'), str(r, 'runbookTitle')),
      route: (r) => `/runbooks/${str(r, 'runbookId')}`,
    },

    // Activity: failure fingerprints and recovery statistics.
    {
      key: 'fingerprint', label: 'Failure Fingerprint', plural: 'Failure Fingerprints', icon: 'symbol-key', view: 'activity', idKey: 'fingerprint',
      list: async () => rowsOf(await api.enumerateBlackBoxFingerprints({ pageNumber: 1, pageSize: 200 } as never)).map((r) => ({ name: str(r, 'title') || str(r, 'summary') || str(r, 'fingerprint'), ...r })),
      describe: (r) => join(`${String(r.count ?? r.entryCount ?? '')} entries`, str(r, 'failureKind')),
      actions: [{ label: 'Show Trend', showResult: 'Fingerprint trend', run: (r) => api.getBlackBoxFingerprintTrend(str(r, 'fingerprint')) }],
      route: () => '/missions?tab=blackbox',
    },
    {
      key: 'recoveryStats', label: 'Recovery Statistics', plural: 'Recovery Statistics', icon: 'graph-line', view: 'activity',
      list: async () => [{ id: 'recovery', name: 'Recovery Statistics', ...asRecord(await api.getBlackBoxRecoveryStats({})) }],
      route: () => '/missions?tab=blackbox',
    },

    // Configuration: Mux endpoints.
    {
      key: 'muxEndpoint', label: 'Mux Endpoint', plural: 'Mux Endpoints', icon: 'plug', view: 'configuration', idKey: 'name',
      list: async () => rowsOf(await api.enumerateMuxEndpoints({ pageNumber: 1, pageSize: 500 })),
      get: async (i) => asRecord(await api.getMuxEndpoint(i)),
      describe: (r) => join(str(r, 'adapterType') || str(r, 'provider'), str(r, 'model')),
      route: () => '/captains',
    },

    // System: storage hosts and black box retention.
    {
      key: 'storageHost', label: 'Storage Host', plural: 'Storage Hosts', icon: 'server-process', view: 'system',
      list: async () => rowsOf(await api.listStorageHosts({ pageNumber: 1, pageSize: 200 } as never)).map((r, i) => ({ id: r.id ?? r.hostId ?? `host-${i}`, name: r.name ?? r.hostName ?? r.hostId ?? `Host ${i + 1}`, ...r })),
      describe: (r) => join(str(r, 'status'), str(r, 'kind')),
      route: () => '/server?tab=storage',
    },
    {
      key: 'retention', label: 'Black Box Retention', plural: 'Black Box Retention', icon: 'trash', view: 'system',
      list: async () => [{ id: 'retention', name: 'Black Box Retention', ...asRecord(await api.getBlackBoxRetention()) }],
      actions: [{ label: 'Backfill Missing Entries', confirm: () => 'Capture black box entries for past failures that have none?', showResult: 'Backfill', run: () => api.backfillBlackBox() }],
      route: () => '/server?tab=storage',
    },
  ];
}

/** Extra actions and create forms for kinds of the first catalog, merged in by key. */
export function extendCatalog(specs: ResourceSpec[]): void {
  extendCatalogFirst(specs);
  extendCatalogMore(specs);
}

function extendCatalogFirst(specs: ResourceSpec[]): void {
  const by = (key: string): ResourceSpec | undefined => specs.find((s) => s.key === key);
  const add = (key: string, extra: Partial<ResourceSpec>): void => {
    const spec = by(key);
    if (!spec) return;
    if (extra.actions) spec.actions = [...(spec.actions ?? []), ...extra.actions];
    if (extra.create && !spec.create) spec.create = extra.create;
    if (extra.createFields && !spec.createFields) spec.createFields = extra.createFields;
  };
  add('fleetAction', {
    create: (d) => api.createFleetAction(d as never),
    createFields: [
      { key: 'name', label: 'Name', type: 'text', required: true },
      { key: 'description', label: 'Description', type: 'text' },
      { key: 'kind', label: 'Kind', type: 'select', value: 'Command', options: ['Command', 'Mission'].map((v) => ({ value: v, label: v })) },
      { key: 'commandText', label: 'Command', type: 'textarea', hint: 'For Command actions: what runs in each vessel.' },
      { key: 'promptTemplate', label: 'Mission brief', type: 'textarea', hint: 'For Mission actions: the brief each vessel\'s mission gets.' },
    ],
  });
  add('fleetActionRun', { actions: [{ label: 'Show Targets', showResult: 'Run targets', run: (r) => api.enumerateFleetActionRunTargets(id(r), { pageNumber: 1, pageSize: 500 }) }] });
  add('runbook', {
    create: (d) => api.createRunbook(d as never),
    createFields: [
      { key: 'title', label: 'Title', type: 'text', required: true },
      { key: 'description', label: 'Description', type: 'textarea' },
      { key: 'overviewMarkdown', label: 'Overview', type: 'textarea' },
      { key: 'steps', label: 'Steps', type: 'textarea', kind: 'json', value: '[\n  { "title": "Step one", "instructions": "" }\n]', hint: 'JSON array of steps.' },
    ],
  });
  add('deployment', {
    create: (d) => api.createDeployment(d as never),
    createFields: [
      { key: 'title', label: 'Title', type: 'text', required: true },
      { key: 'environmentId', label: 'Environment id', type: 'text', required: true },
      { key: 'releaseId', label: 'Release id', type: 'text' },
      { key: 'vesselId', label: 'Vessel id', type: 'text' },
    ],
  });
  add('harbor', { create: (d) => api.createHarbor(d as never), createFields: [{ key: 'name', label: 'Name', type: 'text', required: true }] });
  add('modelEndpoint', {
    create: (d) => api.createModelEndpoint(d as never),
    createFields: [
      { key: 'name', label: 'Name', type: 'text', required: true },
      { key: 'provider', label: 'Provider', type: 'text', required: true, placeholder: 'OpenAI, Anthropic, Ollama, ...' },
      { key: 'baseUrl', label: 'Base URL', type: 'text', required: true },
      { key: 'model', label: 'Model', type: 'text' },
      { key: 'apiKey', label: 'API key', type: 'text' },
    ],
    actions: [
      { label: 'Evaluate (Quick)', confirm: (r) => `Run a quick evaluation of "${str(r, 'name')}"?`, run: (r) => api.evaluateModelEndpoint(id(r), 'Quick') },
      { label: 'Duplicate', run: (r) => api.duplicateModelEndpoint(id(r), { name: `${str(r, 'name')} (copy)` }) },
    ],
  });
  add('cliPermissionRule', {
    create: (d) => api.createCliPermissionRule(d as never),
    createFields: [
      { key: 'pattern', label: 'Pattern', type: 'text', required: true, placeholder: 'Bash(git status*)', hint: 'The tool call the rule matches, as the CLI permission requests show it.' },
      { key: 'action', label: 'Action', type: 'select', value: 'Allow', options: ['Allow', 'Deny'].map((v) => ({ value: v, label: v })) },
      { key: 'scope', label: 'Scope', type: 'select', value: 'Global', options: ['Global', 'Vessel', 'Captain'].map((v) => ({ value: v, label: v })) },
      { key: 'description', label: 'Description', type: 'text' },
    ],
  });
  add('batteryPack', {
    create: (d) => api.createBatteryPack(String(d.packId ?? ''), String(d.manifestJson ?? '{}')),
    createFields: [
      { key: 'packId', label: 'Pack id', type: 'text', required: true },
      { key: 'manifestJson', label: 'Manifest', type: 'textarea', required: true, value: '{\n  "name": "",\n  "tasks": []\n}' },
    ],
  });
  add('vessel', {
    actions: [
      { label: 'Show Branches', showResult: 'Branches', run: (r) => api.getVesselBranches(id(r)) },
      { label: 'Show Landing Preview', showResult: 'Landing preview', run: (r) => api.getVesselLandingPreview(id(r)) },
      { label: 'Show Cleanup Inventory', showResult: 'Cleanup inventory', run: (r) => api.getVesselCleanupInventory(id(r)) },
      { label: 'Show Cleanup Resources', showResult: 'Cleanup resources', run: (r) => api.listVesselCleanupResources(id(r), { pageNumber: 1, pageSize: 200 }) },
      { label: 'Show Dependencies', showResult: 'Dependencies', run: (r) => api.listVesselDependencies(id(r), { pageNumber: 1, pageSize: 200 }) },
      { label: 'Show Commit Activity', showResult: 'Commit activity', run: (r) => api.getVesselCommitActivity(id(r)) },
      {
        label: 'Build Project Context with a Captain...',
        run: async (r) => {
          const captainId = await vscode.window.showInputBox({ title: 'Build project context', prompt: 'Captain id (cpt_...) that reads the repository and writes its context', ignoreFocusOut: true });
          if (!captainId) return undefined;
          const result = await api.buildVesselContext(id(r), { captainId: captainId.trim() });
          void vscode.window.showInformationMessage('Armada: building the project context; the vessel updates when the captain finishes.');
          return result;
        },
      },
    ],
  });
  add('vesselHealth', {
    actions: [
      { label: 'Show Fleet Health Summary', showResult: 'Fleet health', run: () => api.getVesselHealthSummary() },
      {
        label: 'Override a Criterion...',
        run: async (r) => {
          const criterion = await vscode.window.showInputBox({ title: 'Override health', prompt: 'Criterion (for example Dependencies, TestInfra)', ignoreFocusOut: true });
          if (!criterion) return undefined;
          const status = await vscode.window.showQuickPick(['Healthy', 'Warning', 'Critical', 'Unknown'], { title: `Status for ${criterion}` });
          if (!status) return undefined;
          return api.setVesselHealthOverride(str(r, 'vesselId'), criterion as never, status as never);
        },
      },
      { label: 'Clear an Override...', run: async (r) => { const c = await vscode.window.showInputBox({ title: 'Clear override', prompt: 'Criterion', ignoreFocusOut: true }); return c ? api.deleteVesselHealthOverride(str(r, 'vesselId'), c as never) : undefined; } },
    ],
  });
  add('captain', {
    actions: [
      { label: 'Show Tools', showResult: 'Captain tools', run: (r) => api.getCaptainTools(id(r)) },
      { label: 'Capture Usage Now', run: (r) => api.captureCaptainUsage(id(r)) },
    ],
  });
  add('release', { actions: [{ label: 'Show Pull Requests', showResult: 'Release pull requests', run: (r) => api.getReleaseGitHubPullRequests(id(r), { pageNumber: 1, pageSize: 100 }) }] });
  add('stageOutcome', {
    actions: [
      { label: 'Draft a Failure Cause Rule', showResult: 'Draft rule', run: (r) => api.draftFailureCauseRule(id(r)) },
      { label: 'Clear the Classification', confirm: () => 'Clear this outcome\'s cause?', run: (r) => api.classifyStageOutcome(id(r), null) },
    ],
  });
  add('backlog', {
    actions: [{
      label: 'Import from GitHub...',
      run: async () => {
        const kind = await vscode.window.showQuickPick(['Issue', 'PullRequest'], { title: 'Import a GitHub issue or pull request into the backlog' });
        if (!kind) return undefined;
        const number = await vscode.window.showInputBox({ title: `GitHub ${kind} number`, ignoreFocusOut: true, validateInput: (v) => (/^\d+$/.test(v.trim()) ? undefined : 'A number') });
        if (!number) return undefined;
        const vesselId = await vscode.window.showInputBox({ title: 'Vessel id (vsl_...) whose repository has it', ignoreFocusOut: true });
        if (!vesselId) return undefined;
        return api.importObjectiveFromGitHub({ vesselId: vesselId.trim(), sourceType: kind as never, number: Number(number) });
      },
    }],
  });
  add('server', {
    actions: [
      { label: 'Show Rebuild Status', showResult: 'Rebuild status', run: () => api.getRebuildStatus() },
      { label: 'Rebuild from Source', confirm: () => 'Rebuild the Admiral from source and restart it? It rolls back if the new build does not come up.', run: () => api.rebuildServer({}) },
      { label: 'Roll Back the Last Rebuild', confirm: () => 'Roll the Admiral back to the build before the last rebuild?', run: () => api.rollbackServer() },
      { label: 'Stop Admiral', confirm: () => 'Stop the Armada Admiral? Every client loses its connection until it is started again.', run: () => api.stopServer() },
      {
        label: 'Reset Admiral...',
        run: async () => {
          const typed = await vscode.window.showInputBox({ title: 'Reset the Admiral', prompt: 'This deletes the Admiral\'s data. Type RESET to confirm.', ignoreFocusOut: true });
          if (typed !== 'RESET') return undefined;
          return api.resetServer();
        },
      },
    ],
  });
  add('checkRun', {
    create: (d) => api.importCheckRun(d as never),
    createFields: [
      { key: 'vesselId', label: 'Vessel id', type: 'text', required: true, placeholder: 'vsl_...' },
      { key: 'type', label: 'Type', type: 'select', value: 'UnitTest', options: CHECK_TYPES.map((v) => ({ value: v, label: v })) },
      { key: 'status', label: 'Status', type: 'select', value: 'Passed', options: ['Pending', 'Running', 'Passed', 'Failed', 'Canceled'].map((v) => ({ value: v, label: v })) },
      { key: 'label', label: 'Label', type: 'text' },
      { key: 'externalUrl', label: 'External URL', type: 'text', hint: 'Where the run lives (a CI job, for example).' },
      { key: 'branchName', label: 'Branch', type: 'text' },
    ],
  });

}

function extendCatalogMore(specs: ResourceSpec[]): void {
  const by = (key: string): ResourceSpec | undefined => specs.find((s) => s.key === key);
  const add = (key: string, extra: Partial<ResourceSpec>): void => {
    const spec = by(key);
    if (!spec) return;
    if (extra.actions) spec.actions = [...(spec.actions ?? []), ...extra.actions];
    if (extra.create && !spec.create) spec.create = extra.create;
    if (extra.createFields && !spec.createFields) spec.createFields = extra.createFields;
  };
  const ask = async (title: string, prompt: string, value = ''): Promise<string | undefined> => {
    const answer = await vscode.window.showInputBox({ title, prompt, value, ignoreFocusOut: true });
    return answer?.trim() || undefined;
  };
  add('mission', {
    actions: [
      { label: 'Retry Landing', when: (r) => str(r, 'status') === 'LandingFailed', run: (r) => api.retryMissionLanding(id(r)) },
      { label: 'Preview Captain Selection', showResult: 'Captain selection preview', run: (r) => api.previewCaptainSelection(id(r)) },
      { label: 'Purge Mission', confirm: (r) => `Purge "${str(r, 'title')}"? Its record, logs and dock are deleted for good.`, run: (r) => api.purgeMission(id(r)) },
    ],
  });
  add('voyage', { actions: [{ label: 'Purge Voyage', confirm: (r) => `Purge the voyage "${str(r, 'title')}" and its missions for good?`, run: (r) => api.purgeVoyage(id(r)) }] });
  add('captain', {
    actions: [
      { label: 'Recall', when: (r) => str(r, 'state') !== 'Idle', confirm: (r) => `Recall "${str(r, 'name')}"? Its process stops and it returns to Idle.`, run: (r) => api.recallCaptain(id(r)) },
      { label: 'Release from Quarantine', when: (r) => str(r, 'state') === 'Quarantined', run: (r) => api.unquarantineCaptain(id(r)) },
      {
        label: 'Send Signal...',
        run: async (r) => {
          const payload = await ask(`Signal ${str(r, 'name')}`, 'Message for the captain');
          return payload ? api.sendSignal({ toCaptainId: id(r), type: 'Mail', payload }) : undefined;
        },
      },
      {
        label: 'Set CLI Tool Permission Policy...',
        run: async (r) => {
          const policy = await vscode.window.showQuickPick([
            { label: 'Ask in Armada', description: 'tool calls wait for approval in Needs You', value: 'ApproveInArmada' as const },
            { label: 'Refuse', description: 'tool calls that need permission are refused', value: 'Refuse' as const },
            { label: 'Bypass', description: 'the CLI runs tools without asking', value: 'Bypass' as const },
            { label: 'Use the server default', value: null },
          ], { title: `CLI tool permissions for ${str(r, 'name')}` });
          return policy ? api.setCaptainCliPermissionPolicy(id(r), policy.value) : undefined;
        },
      },
      {
        label: 'Chat with Captain...',
        run: async (r) => {
          const message = await ask(`Chat with ${str(r, 'name')}`, 'One question for the captain (it runs its CLI once to answer)');
          if (!message) return undefined;
          const reply = await vscode.window.withProgress({ location: vscode.ProgressLocation.Notification, title: `Armada: ${str(r, 'name')} is answering...` }, () => api.chatWithCaptain(id(r), { message, history: [] }));
          await vscode.window.showTextDocument(await vscode.workspace.openTextDocument({ language: 'markdown', content: `**You:** ${message}\n\n**${str(r, 'name')}${reply.model ? ` (${reply.model})` : ''}:**\n\n${reply.success ? reply.reply : `Error: ${reply.error ?? 'no reply'}`}` }), { preview: false });
          return reply;
        },
      },
    ],
  });
  add('vessel', {
    actions: [
      { label: 'Show Workspace Status', showResult: 'Workspace status', run: (r) => api.getWorkspaceStatus(id(r)) },
      { label: 'Show Changed Files', showResult: 'Changed files', run: (r) => api.listAllWorkspaceChanges(id(r)) },
      { label: 'Show Missions Working Here', showResult: 'Active missions', run: (r) => api.listAllWorkspaceActiveMissions(id(r)) },
      { label: 'Show Effective Workflow Profile', showResult: 'Workflow profile', run: (r) => api.resolveWorkflowProfile(id(r)) },
      { label: 'Preview Workflow Profile Resolution', showResult: 'Workflow profile resolution', run: (r) => api.previewWorkflowProfileForVessel(id(r)) },
      { label: 'Show Effective Project Profile', showResult: 'Project profile', run: (r) => api.resolveProjectProfileForVessel(id(r)) },
      {
        label: 'Push a Branch...',
        run: async (r) => {
          const branch = await ask(`Push a branch of ${str(r, 'name')}`, 'Branch to push to the remote');
          return branch ? api.pushVesselBranch(id(r), branch) : undefined;
        },
      },
      {
        label: 'Merge a Branch...',
        run: async (r) => {
          const source = await ask(`Merge into ${str(r, 'name')}`, 'Source branch');
          if (!source) return undefined;
          const target = await ask(`Merge ${source}`, 'Target branch', str(r, 'defaultBranch') || 'main');
          if (!target) return undefined;
          const push = (await vscode.window.showQuickPick(['Merge and push', 'Merge only'], { title: `Merge ${source} into ${target}` })) === 'Merge and push';
          return api.mergeVesselBranch(id(r), source, target, push);
        },
      },
      {
        label: 'Clean Up...',
        run: async (r) => {
          const inventory = await api.listVesselCleanupResources(id(r), { pageNumber: 1, pageSize: 500 });
          const rows = rowsOf(inventory);
          if (!rows.length) {
            void vscode.window.showInformationMessage('Armada: nothing to clean up.');
            return undefined;
          }
          const picked = await vscode.window.showQuickPick(rows.map((x) => ({ label: str(x, 'name') || str(x, 'key'), description: str(x, 'kind') || str(x, 'type'), detail: str(x, 'reason') || str(x, 'disposition'), key: str(x, 'key') })), { title: 'Clean up these', canPickMany: true });
          if (!picked?.length) return undefined;
          if ((await vscode.window.showWarningMessage(`Clean up ${picked.length} resources of ${str(r, 'name')}?`, { modal: true }, 'Clean Up')) !== 'Clean Up') return undefined;
          return api.runVesselCleanup(id(r), picked.map((x) => x.key));
        },
        showResult: 'Cleanup',
      },
      {
        label: 'Run a Check...',
        run: async (r) => {
          const type = await vscode.window.showQuickPick(CHECK_TYPES, { title: `Run a check on ${str(r, 'name')}` });
          if (!type) return undefined;
          return vscode.window.withProgress({ location: vscode.ProgressLocation.Notification, title: `Armada: running ${type}...` }, () => api.runCheck({ vesselId: id(r), type: type as never }));
        },
        showResult: 'Check run',
      },
      {
        label: 'Sync GitHub Actions Runs',
        showResult: 'GitHub Actions sync',
        run: (r) => api.syncGitHubActions({ vesselId: id(r), runCount: 20 }),
      },
    ],
  });
  add('fleetAction', {
    actions: [{
      label: 'Run a Command on Vessels...',
      when: () => true,
      run: async () => {
        const command = await ask('Run a command across vessels', 'Shell command for each vessel\'s checkout');
        if (!command) return undefined;
        const vesselIds = await vscode.commands.executeCommand<string[] | undefined>('armada.pickVessels', 'Run on');
        if (!vesselIds?.length) return undefined;
        return api.runAdHocFleetAction({ VesselIds: vesselIds, Definition: { Name: command.slice(0, 60), Kind: 'Command' as never, CommandText: command } as never });
      },
    }],
  });
  add('backlog', {
    actions: [
      { label: 'Move to the Top', run: (r) => api.reorderBacklog({ items: [{ objectiveId: id(r), rank: 0 }] }) },
      { label: 'Move to the Bottom', run: (r) => api.reorderBacklog({ items: [{ objectiveId: id(r), rank: 1000000 }] }) },
      { label: 'Show Refinement Sessions', showResult: 'Refinement sessions', run: (r) => api.listObjectiveRefinementSessions(id(r), { pageNumber: 1, pageSize: 50 } as never) },
    ],
  });
  add('harbor', {
    actions: [{
      label: 'Stop a Job...',
      run: async (r) => {
        const jobs = rowsOf(await api.listHarborJobs(id(r), { openOnly: true, pageNumber: 1, pageSize: 200 }));
        if (!jobs.length) {
          void vscode.window.showInformationMessage('Armada: this Harbor has no running jobs.');
          return undefined;
        }
        const picked = await vscode.window.showQuickPick(jobs.map((j) => ({ label: str(j, 'kind') || str(j, 'jobId') || id(j), description: str(j, 'status'), detail: str(j, 'missionId') || str(j, 'description'), jobId: str(j, 'jobId') || id(j) })), { title: 'Stop which job?' });
        if (!picked) return undefined;
        return api.stopHarborJob(id(r), picked.jobId);
      },
    }],
  });
  add('batteryPack', {
    actions: [
      {
        label: 'Open a File...',
        run: async (r) => {
          const detail = asRecord(await api.getBatteryPack(id(r)));
          const files = ((detail.files ?? []) as Array<{ path?: string } | string>).map((f) => (typeof f === 'string' ? f : f.path ?? '')).filter(Boolean);
          const path = files.length ? await vscode.window.showQuickPick(files, { title: 'Open which file?' }) : await ask('Open a pack file', 'Path inside the pack');
          if (!path) return undefined;
          const file = await api.readBatteryPackFile(id(r), path);
          await vscode.window.showTextDocument(await vscode.workspace.openTextDocument({ content: String((file as unknown as { content?: string }).content ?? '') }), { preview: false });
          return file;
        },
      },
      {
        label: 'Delete a File...',
        run: async (r) => {
          const path = await ask('Delete a pack file', 'Path inside the pack');
          if (!path) return undefined;
          if ((await vscode.window.showWarningMessage(`Delete ${path} from the pack?`, { modal: true }, 'Delete')) !== 'Delete') return undefined;
          return api.deleteBatteryPackFile(id(r), path);
        },
      },
    ],
  });
  add('persona', {
    actions: [{
      label: 'Preview Prompt for a Project Profile...',
      run: async (r) => {
        const profileId = await ask('Preview the persona prompt', 'Project profile id');
        return profileId ? api.previewPersonaPrompt(profileId, str(r, 'name')) : undefined;
      },
      showResult: 'Persona prompt',
    }],
  });
  add('blackBoxEntry', {
    actions: [
      { label: 'Pin (keep past retention)', when: (r) => r.pinned !== true, run: (r) => api.pinBlackBox(id(r)) },
      { label: 'Unpin', when: (r) => r.pinned === true, run: (r) => api.unpinBlackBox(id(r)) },
      { label: 'Analyze Again', run: (r) => api.reanalyzeBlackBox(id(r)) },
      {
        label: 'Read a Captured Object...',
        run: async (r) => {
          const objects = rowsOf(await api.listBlackBoxObjects(id(r), { pageNumber: 1, pageSize: 200 }));
          const picked = await vscode.window.showQuickPick(objects.map((o) => ({ label: str(o, 'name'), description: `${String(o.sizeBytes ?? '')} bytes`, name: str(o, 'name') })), { title: 'Read which object?' });
          if (!picked) return undefined;
          const read = await api.readBlackBoxObject(id(r), picked.name);
          await vscode.window.showTextDocument(await vscode.workspace.openTextDocument({ content: String((read as unknown as { content?: string; text?: string }).content ?? (read as unknown as { text?: string }).text ?? '') }), { preview: false });
          return read;
        },
      },
    ],
  });
  add('retention', {
    actions: [
      { label: 'Preview Garbage Collection', showResult: 'Garbage collection (dry run)', run: () => api.runBlackBoxGc(true) },
      { label: 'Run Garbage Collection', confirm: () => 'Delete black box data past its retention now?', showResult: 'Garbage collection', run: () => api.runBlackBoxGc(false) },
      {
        label: 'Purge Entries...',
        run: async () => {
          const days = await ask('Purge black box entries', 'Delete unpinned entries older than this many days', '180');
          if (!days || !/^\d+$/.test(days)) return undefined;
          if ((await vscode.window.showWarningMessage(`Delete every unpinned black box entry older than ${days} days?`, { modal: true }, 'Purge')) !== 'Purge') return undefined;
          return api.purgeBlackBox({ olderThanDays: Number(days) } as never);
        },
        showResult: 'Purge',
      },
    ],
  });
  add('storage', {
    actions: [
      { label: 'Show Storage Summary', showResult: 'Storage', run: () => api.getStorage() },
      { label: 'Refresh Storage Measurements', showResult: 'Storage', run: () => api.refreshStorage() },
      {
        label: 'Migrate Black Box Storage...',
        run: async () => {
          const target = await ask('Migrate black box storage', 'Target storage id (from the storage targets listed on this page)');
          if (!target) return undefined;
          if ((await vscode.window.showWarningMessage(`Move black box storage to ${target}?`, { modal: true }, 'Migrate')) !== 'Migrate') return undefined;
          return api.migrateBlackBoxStorage({ id: target } as never);
        },
        showResult: 'Migration',
      },
      {
        label: 'Show Migration Status...',
        run: async (r) => {
          const migration = str(r, 'activeMigrationId') || (await ask('Migration status', 'Migration id'));
          return migration ? api.getBlackBoxStorageMigration(migration) : undefined;
        },
        showResult: 'Migration',
      },
    ],
  });
  add('captainUsage', {
    actions: [
      { label: 'Show Snapshot History', showResult: 'Usage snapshots', run: () => api.getUsageSnapshots({}) },
      { label: 'Show Usage by Mission', showResult: 'Usage by mission', run: () => api.getUsageMissions({} as never) },
      { label: 'Show Usage Series', showResult: 'Usage series', run: () => api.getUsageSeries({} as never) },
    ],
  });
  const requestHistory = by('requestHistory');
  if (requestHistory) requestHistory.remove = (i) => api.deleteRequestHistoryEntry(i);

  const set = (key: string, fn: (spec: ResourceSpec) => void): void => {
    const spec = by(key);
    if (spec) fn(spec);
  };
  set('mission', (spec) => {
    spec.update = (i, d) => api.updateMission(i, d as never);
    spec.editable = ['title', 'description', 'priority', 'persona'];
  });
  set('deployment', (spec) => {
    spec.update = (i, d) => api.updateDeployment(i, d as never);
    spec.editable = ['title', 'summary', 'notes', 'sourceRef'];
  });
  add('vessel', {
    actions: [{
      label: 'Edit Project Context...',
      run: async (r) => {
        const saved = await showForm<boolean>({
          title: `Project context: ${str(r, 'name')}`,
          intro: 'Captains read this with every mission on the vessel.',
          submitLabel: 'Save',
          fields: [
            { key: 'projectContext', label: 'Project context', type: 'textarea', value: str(r, 'projectContext') },
            { key: 'styleGuide', label: 'Style guide', type: 'textarea', value: str(r, 'styleGuide') },
            { key: 'modelContext', label: 'Model context', type: 'textarea', value: str(r, 'modelContext') },
          ],
        }, async (values) => {
          try {
            await api.updateVesselContext(id(r), { projectContext: values.projectContext || null, styleGuide: values.styleGuide || null, modelContext: values.modelContext || null });
            return { result: true };
          } catch (err) {
            return { error: describeError(err) };
          }
        });
        return saved;
      },
    }],
  });
  add('modelEndpoint', { actions: [{ label: 'Validate', showResult: 'Validation', run: (r) => api.validateModelEndpoint(id(r)) }] });
  add('projectProfile', { actions: [{ label: 'Validate', showResult: 'Validation', run: (r) => api.validateProjectProfile(r as never) }] });
  add('workflowProfile', { actions: [{ label: 'Validate', showResult: 'Validation', run: (r) => api.validateWorkflowProfile(r as never) }] });
  add('batteryPack', {
    actions: [
      { label: 'Validate', showResult: 'Validation', run: (r) => api.validateBatteryPack(id(r)) },
      {
        label: 'Edit Manifest...',
        run: async (r) => {
          const detail = asRecord(await api.getBatteryPack(id(r)));
          const current = str(detail, 'manifestJson') || JSON.stringify(detail.manifest ?? {}, null, 2);
          return showForm<boolean>({ title: `Manifest: ${str(r, 'name') || id(r)}`, submitLabel: 'Save', fields: [{ key: 'manifest', label: 'Manifest (JSON)', type: 'textarea', value: current, required: true }] }, async (values) => {
            try {
              JSON.parse(values.manifest);
              await api.updateBatteryPackManifest(id(r), values.manifest);
              return { result: true };
            } catch (err) {
              return { error: describeError(err) };
            }
          });
        },
      },
      {
        label: 'Write a File...',
        run: async (r) => {
          const path = await ask('Write a pack file', 'Path inside the pack (created or replaced)');
          if (!path) return undefined;
          return showForm<boolean>({ title: `${path} in ${str(r, 'name') || id(r)}`, submitLabel: 'Save', fields: [{ key: 'content', label: 'Content', type: 'textarea', required: true }] }, async (values) => {
            try {
              await api.writeBatteryPackFile(id(r), path, values.content);
              return { result: true };
            } catch (err) {
              return { error: describeError(err) };
            }
          });
        },
      },
    ],
  });
  add('fleetActionRun', {
    actions: [{
      label: 'Show a Target\'s Output...',
      run: async (r) => {
        const targets = rowsOf(await api.enumerateFleetActionRunTargets(id(r), { pageNumber: 1, pageSize: 500 }));
        const picked = await vscode.window.showQuickPick(targets.map((t) => ({ label: str(t, 'vesselName') || str(t, 'vesselId'), description: str(t, 'status'), targetId: id(t) })), { title: 'Which vessel?' });
        return picked ? api.getFleetActionRunTarget(id(r), picked.targetId) : undefined;
      },
      showResult: 'Run target',
    }],
  });
  add('mission', {
    actions: [
      {
        label: 'Show the Selection Decision',
        run: async (r) => {
          const summary = asRecord(await api.getCaptainSelection(id(r)));
          const decisionId = str(summary, 'decisionId') || str(asRecord(summary.latestDecision), 'id') || str(asRecord(summary.decision), 'id');
          if (!decisionId) return summary;
          const [decision, candidates] = await Promise.all([api.getCaptainSelectionDecision(decisionId), api.getCaptainSelectionCandidates(decisionId, undefined, 1, 50)]);
          return { decision, candidates };
        },
        showResult: 'Captain selection decision',
      },
      {
        label: 'Authorize Selection Recovery...',
        run: async (r) => {
          const summary = asRecord(await api.getCaptainSelection(id(r)));
          const reason = await ask('Authorize captain selection recovery', 'Why this mission may use a fallback captain or a reserved account');
          if (!reason) return undefined;
          const allowFallback = (await vscode.window.showQuickPick(['Allow a fallback captain', 'Do not allow a fallback'], { title: 'Fallback' })) === 'Allow a fallback captain';
          const reserveException = (await vscode.window.showQuickPick(['Allow using reserved capacity', 'Keep the reserve'], { title: 'Reserve' })) === 'Allow using reserved capacity';
          return api.authorizeCaptainSelectionRecovery(id(r), { expectedRevision: Number(summary.revision ?? summary.selectionRevision ?? 0), allowFallback, reserveException, reason });
        },
        showResult: 'Selection recovery',
      },
    ],
  });
  specs.push({
    key: 'importBatch', label: 'Import Batch', plural: 'Vessel Import Batches', icon: 'cloud-download', view: 'activity',
    list: async () => rowsOf(await api.enumerateVesselImportBatches({ pageNumber: 1, pageSize: 100 })),
    get: async (i) => asRecord(await api.getVesselImportBatch(i)),
    describe: (r) => join(str(r, 'status'), `${String(r.importedCount ?? r.itemCount ?? '')}`),
    actions: [
      {
        label: 'Sort into Fleets with a Captain...',
        run: async (r) => {
          const captainId = await ask('Sort imported vessels into fleets', 'Captain id (cpt_...) that proposes the fleets');
          if (!captainId) return undefined;
          const prompt = asRecord(await api.getFleetCategorizationDefaultPrompt());
          return api.categorizeVesselImport(id(r), { Enabled: true, CaptainId: captainId, Prompt: str(prompt, 'prompt') || null, ApplyAutomatically: false });
        },
        showResult: 'Fleet recommendations',
      },
      {
        label: 'Apply Fleet Recommendations',
        confirm: () => 'Create the recommended fleets and move the imported vessels into them?',
        run: async (r) => {
          const batch = asRecord(await api.getVesselImportBatch(id(r)));
          const recommendations = (asRecord(batch.batch).fleetRecommendations ?? batch.fleetRecommendations ?? []) as Array<{ name?: string; description?: string; vesselIds?: string[] }>;
          if (!recommendations.length) {
            void vscode.window.showInformationMessage('Armada: this batch has no fleet recommendations yet; sort it first.');
            return undefined;
          }
          return api.applyFleetRecommendations(id(r), { Fleets: recommendations.map((x) => ({ Name: x.name ?? 'Fleet', Description: x.description ?? null, VesselIds: x.vesselIds ?? [] })) });
        },
        showResult: 'Fleets applied',
      },
    ],
    route: () => '/vessels/import',
  });

  add('backlog', {
    actions: [{
      label: 'Delete a Refinement Session...',
      run: async (r) => {
        const sessions = rowsOf(await api.listObjectiveRefinementSessions(id(r), { pageNumber: 1, pageSize: 100 } as never));
        if (!sessions.length) {
          void vscode.window.showInformationMessage('Armada: this item has no refinement sessions.');
          return undefined;
        }
        const picked = await vscode.window.showQuickPick(sessions.map((x) => ({ label: str(x, 'title') || id(x), description: str(x, 'status'), id: id(x) })), { title: 'Delete which refinement session?' });
        if (!picked) return undefined;
        if ((await vscode.window.showWarningMessage(`Delete the refinement session "${picked.label}"?`, { modal: true }, 'Delete')) !== 'Delete') return undefined;
        return api.deleteObjectiveRefinementSession(picked.id);
      },
    }],
  });
}
