import { act, fireEvent, screen, waitFor } from '@testing-library/react-native';
import * as client from '@dashboard/api/client';
import type { Harbor, Memory, ModelEndpoint, Pipeline, Playbook, ProjectProfile, WorkflowProfile } from '@dashboard/types/models';
import MoreLayout from '../app/(app)/(more)/_layout';
import ConfigurationRoute from '../app/(app)/(more)/configuration';
import PipelineRoute from '../app/(app)/(more)/pipelines/[name]';
import PlaybookRoute from '../app/(app)/(more)/playbooks/[id]';
import ProjectProfileRoute from '../app/(app)/(more)/project-profiles/[id]';
import TemplateCreateRoute from '../app/(app)/(more)/prompt-templates/create';
import TemplateRoute from '../app/(app)/(more)/prompt-templates/[name]';
import SkillRoute from '../app/(app)/(more)/skills/[id]';
import WorkflowRoute from '../app/(app)/(more)/workflow-profiles/[id]';
import { page, renderW4Routes, resetW4 } from '../test/w4';
import { rowActionTarget } from '../test/a11y';

jest.mock('@dashboard/api/client', () => require('../test/w4Client').autoMockClient());

const api = client as jest.Mocked<typeof client>;

const ROUTES = {
  '(more)/_layout': MoreLayout,
  '(more)/more': () => null,
  '(more)/configuration': ConfigurationRoute,
  '(more)/pipelines/[name]': PipelineRoute,
  '(more)/playbooks/[id]': PlaybookRoute,
  '(more)/project-profiles/[id]': ProjectProfileRoute,
  '(more)/prompt-templates/create': TemplateCreateRoute,
  '(more)/prompt-templates/[name]': TemplateRoute,
  '(more)/skills/[id]': SkillRoute,
  '(more)/workflow-profiles/[id]': WorkflowRoute,
  'voyages/[id]': () => null,
};

const STAMP = { createdUtc: '2026-10-01T00:00:00Z', lastUpdateUtc: '2026-10-02T00:00:00Z' };

function workflow(over: Partial<WorkflowProfile> = {}): WorkflowProfile {
  return {
    id: 'wfp_1', tenantId: 'ten_1', userId: 'usr_9', ownershipScope: 'TenantWide', name: 'Dotnet', description: null, scope: 'Global', fleetId: null, vesselId: null,
    isDefault: true, active: true, languageHints: ['dotnet'], lintCommand: null, buildCommand: 'dotnet build', unitTestCommand: 'dotnet test', integrationTestCommand: null,
    e2eTestCommand: null, migrationCommand: null, securityScanCommand: null, performanceCommand: null, packageCommand: null, deploymentVerificationCommand: null,
    rollbackVerificationCommand: null, publishArtifactCommand: null, releaseVersioningCommand: null, changelogGenerationCommand: null, requiredSecrets: [],
    requiredInputs: [], expectedArtifacts: [], environments: [], ...STAMP, ...over,
  };
}

const PIPELINE: Pipeline = {
  id: 'ppl_1', tenantId: 'ten_1', userId: null, scope: 'TenantWide', name: 'Reviewed', description: null, isBuiltIn: false, active: true, ...STAMP,
  stages: [
    { id: 's1', pipelineId: 'ppl_1', order: 1, personaName: 'Architect', isOptional: false, description: null, requiresReview: false, reviewDenyAction: 'RetryStage' },
    { id: 's2', pipelineId: 'ppl_1', order: 2, personaName: 'Worker', isOptional: false, description: null, requiresReview: true, reviewDenyAction: 'RetryStage' },
  ],
};

const PROJECT: ProjectProfile = {
  id: 'prp_1', tenantId: 'ten_1', userId: 'usr_1', ownershipScope: 'TenantWide', name: 'Web', description: null, scope: 'Global', fleetId: null, vesselId: null,
  isDefault: false, active: true, defaultPipelineId: null, workflowProfileId: null, personaOverrides: [], skills: ['tdd'], ...STAMP,
};

beforeEach(async () => {
  await resetW4();
  jest.clearAllMocks();
  for (const fn of [api.listFleets, api.listVessels, api.listPipelines, api.listPersonas, api.listPromptTemplates, api.listCaptains]) fn.mockResolvedValue(page([]) as never);
  api.listVessels.mockResolvedValue(page([{ id: 'vsl_1', name: 'armada', active: true }]) as never);
  api.listPersonas.mockResolvedValue(page([{ name: 'Architect' }, { name: 'Worker' }, { name: 'Judge' }]) as never);
  api.listWorkflowProfiles.mockResolvedValue(page([workflow()]) as never);
});

describe('Configuration hub', () => {
  it('opens on Workflow Profiles; a regular user cannot edit a tenant-wide profile', async () => {
    await renderW4Routes(ROUTES, '/configuration', 'user');
    await waitFor(() => expect(screen.getByTestId('workflow-row-wfp_1')).toBeTruthy());
    expect(screen.getByTestId('configuration-tab-memory')).toBeTruthy();
    expect(screen.queryByTestId('workflow-row-wfp_1-swipe-edit')).toBeNull();
    expect(screen.getByTestId('workflow-row-wfp_1-swipe-duplicate')).toBeTruthy();
  });

  it('creates a workflow profile from the list', async () => {
    api.createWorkflowProfile.mockResolvedValue(workflow({ id: 'wfp_2', name: 'Node' }));
    api.getWorkflowProfile.mockResolvedValue(workflow({ id: 'wfp_2', name: 'Node' }));
    await renderW4Routes(ROUTES, '/configuration?tab=workflow-profiles');
    await waitFor(() => expect(screen.getByTestId('workflow-profiles-create')).toBeTruthy());
    await fireEvent.press(screen.getByTestId('workflow-profiles-create'));
    await waitFor(() => expect(screen.getByTestId('workflow-form-name')).toBeTruthy());
    await fireEvent.changeText(screen.getByTestId('workflow-form-name'), 'Node');
    await fireEvent.changeText(screen.getByTestId('workflow-form-buildCommand'), 'npm run build');
    await act(async () => { await fireEvent.press(screen.getByTestId('workflow-form-submit')); });
    expect(api.createWorkflowProfile).toHaveBeenCalledWith(expect.objectContaining({ name: 'Node', buildCommand: 'npm run build', scope: 'Global', requiredInputs: [], environments: [] }));
  });
});

describe('workflow profile detail', () => {
  it('adds environment commands, validates, and previews for a vessel', async () => {
    api.getWorkflowProfile.mockResolvedValue(workflow({ userId: 'usr_1' }));
    api.updateWorkflowProfile.mockImplementation(async (_id, body) => workflow({ environments: body.environments ?? [] }));
    api.validateWorkflowProfile.mockResolvedValue({ isValid: false, errors: ['Missing lint'], warnings: [], availableCheckTypes: ['Build'], commandPreviews: [{ checkType: 'Build', environmentName: null, command: 'dotnet build' }] });
    api.previewWorkflowProfileForVessel.mockResolvedValue({ resolvedProfile: workflow(), resolutionMode: 'Global', availableCheckTypes: ['Build'], commandPreviews: [] } as never);
    await renderW4Routes(ROUTES, '/workflow-profiles/wfp_1');
    await waitFor(() => expect(screen.getByTestId('workflow-title')).toHaveTextContent('Dotnet'));
    await fireEvent.press(screen.getByTestId('workflow-add-environment'));
    await waitFor(() => expect(screen.getByTestId('workflow-environment-form-deployCommand')).toBeTruthy());
    await fireEvent.changeText(screen.getByTestId('workflow-environment-form-deployCommand'), './deploy.sh');
    await act(async () => { await fireEvent.press(screen.getByTestId('workflow-environment-form-submit')); });
    expect(api.updateWorkflowProfile).toHaveBeenCalledWith('wfp_1', expect.objectContaining({ environments: [expect.objectContaining({ environmentName: 'dev', deployCommand: './deploy.sh' })] }));
    await act(async () => { await fireEvent.press(screen.getByTestId('workflow-validate')); });
    await waitFor(() => expect(screen.getByText('Missing lint')).toBeTruthy());
    await fireEvent.press(screen.getByTestId('workflow-preview'));
    await fireEvent.press(screen.getByTestId('workflow-preview-form-vesselId'));
    await fireEvent.press(screen.getByTestId('workflow-preview-form-vesselId-option-vsl_1'));
    await act(async () => { await fireEvent.press(screen.getByTestId('workflow-preview-form-submit')); });
    expect(api.previewWorkflowProfileForVessel).toHaveBeenCalledWith('vsl_1', 'wfp_1');
  });

  it('new is prefilled from the link', async () => {
    api.createWorkflowProfile.mockResolvedValue(workflow({ id: 'wfp_3' }));
    api.getWorkflowProfile.mockResolvedValue(workflow({ id: 'wfp_3' }));
    const h = await renderW4Routes(ROUTES, '/workflow-profiles/new?scope=Vessel&vesselId=vsl_1');
    await waitFor(() => expect(screen.getByTestId('workflow-form-submit')).toBeTruthy());
    await act(async () => { await fireEvent.press(screen.getByTestId('workflow-form-submit')); });
    expect(api.createWorkflowProfile).toHaveBeenCalledWith(expect.objectContaining({ scope: 'Vessel', vesselId: 'vsl_1', name: 'Default Workflow' }));
    await waitFor(() => expect(h.getPathname()).toBe('/workflow-profiles/wfp_3'));
  });
});

describe('project profile detail', () => {
  it('adds a persona override and previews the persona prompt', async () => {
    api.getProjectProfile.mockResolvedValue(PROJECT);
    api.updateProjectProfile.mockImplementation(async (_id, body) => ({ ...PROJECT, personaOverrides: body.personaOverrides ?? [] }));
    api.previewPersonaPrompt.mockResolvedValue({ personaName: 'Architect', baseTemplateName: 'a', effectiveTemplateName: 'b', basePrompt: 'BASE', effectivePrompt: 'EFFECTIVE', additionalInstructions: null, isOverridden: true });
    await renderW4Routes(ROUTES, '/project-profiles/prp_1');
    await waitFor(() => expect(screen.getByTestId('project-title')).toHaveTextContent('Web'));
    await fireEvent.press(screen.getByTestId('project-add-override'));
    await waitFor(() => expect(screen.getByTestId('override-form-additionalInstructions')).toBeTruthy());
    await fireEvent.changeText(screen.getByTestId('override-form-additionalInstructions'), 'Prefer small PRs');
    await act(async () => { await fireEvent.press(screen.getByTestId('override-form-submit')); });
    expect(api.updateProjectProfile).toHaveBeenCalledWith('prp_1', expect.objectContaining({ skills: ['tdd'], personaOverrides: [{ personaName: 'Architect', promptTemplateName: null, additionalInstructions: 'Prefer small PRs', enabled: true }] }));
    await fireEvent.press(screen.getByTestId('project-preview'));
    await act(async () => { await fireEvent.press(screen.getByTestId('project-preview-form-submit')); });
    expect(api.previewPersonaPrompt).toHaveBeenCalledWith('prp_1', 'Architect');
    await waitFor(() => expect(screen.getByText('EFFECTIVE')).toBeTruthy());
  });
});

describe('pipeline detail', () => {
  it('moves a stage and runs the pipeline as a voyage', async () => {
    api.getPipeline.mockResolvedValue(PIPELINE);
    api.updatePipeline.mockResolvedValue(PIPELINE);
    api.createVoyage.mockResolvedValue({ id: 'vyg_1' } as never);
    const h = await renderW4Routes(ROUTES, '/pipelines/Reviewed');
    await waitFor(() => expect(screen.getByTestId('pipeline-title')).toHaveTextContent('Reviewed'));
    await act(async () => { await fireEvent(rowActionTarget(screen.getByTestId('pipeline-stage-1-swipe'), 'up'), 'accessibilityAction', { nativeEvent: { actionName: 'up' } }); });
    expect(api.updatePipeline).toHaveBeenCalledWith('Reviewed', { description: null, stages: [
      { personaName: 'Worker', isOptional: false, description: null, requiresReview: true, reviewDenyAction: 'RetryStage', order: 1 },
      { personaName: 'Architect', isOptional: false, description: null, requiresReview: false, reviewDenyAction: 'RetryStage', order: 2 },
    ] });
    await fireEvent.press(screen.getByTestId('pipeline-run'));
    await waitFor(() => expect(screen.getByTestId('pipeline-run-form-submit')).toBeTruthy());
    await act(async () => { await fireEvent.press(screen.getByTestId('pipeline-run-form-submit')); });
    expect(api.createVoyage).toHaveBeenCalledWith({ title: 'Run: Reviewed', vesselId: 'vsl_1', pipeline: 'Reviewed', missions: [{ vesselId: 'vsl_1', title: 'Run: Reviewed', description: undefined }] });
    await waitFor(() => expect(h.getPathname()).toBe('/voyages/vyg_1'));
  });
});

describe('prompt templates', () => {
  it('create checks the dashboard rules, then creates and opens the template', async () => {
    api.createPromptTemplate.mockResolvedValue({ name: 'my.template' } as never);
    api.getPromptTemplate.mockResolvedValue({ id: 'ptp_1', tenantId: 'ten_1', userId: 'usr_1', scope: 'UserSpecific', name: 'my.template', description: null, category: 'mission', content: 'Hi {MissionTitle}', isBuiltIn: false, active: true, ...STAMP });
    const h = await renderW4Routes(ROUTES, '/prompt-templates/create');
    await waitFor(() => expect(screen.getByTestId('template-form-name')).toBeTruthy());
    expect(screen.getByText('Mission Context')).toBeTruthy();
    await fireEvent.changeText(screen.getByTestId('template-form-name'), 'my.template');
    await act(async () => { await fireEvent.press(screen.getByTestId('template-form-submit')); });
    expect(screen.getByTestId('template-form-error')).toHaveTextContent('Content is required.');
    await fireEvent.changeText(screen.getByTestId('template-form-content'), 'Hi {MissionTitle}');
    await act(async () => { await fireEvent.press(screen.getByTestId('template-form-submit')); });
    expect(api.createPromptTemplate).toHaveBeenCalledWith({ name: 'my.template', category: 'mission', content: 'Hi {MissionTitle}', description: undefined, active: true });
    await waitFor(() => expect(h.getPathname()).toBe('/prompt-templates/my.template'));
  });
});

describe('playbooks and skills', () => {
  it('playbook detail shows stats; skill new creates', async () => {
    const playbook: Playbook = { id: 'pbk_1', tenantId: 'ten_1', userId: 'usr_1', scope: 'UserSpecific', fileName: 'RULES.md', description: null, content: '# Rules\nBe nice', active: true, ...STAMP };
    api.getPlaybook.mockResolvedValue(playbook);
    await renderW4Routes(ROUTES, '/playbooks/pbk_1', 'user');
    await waitFor(() => expect(screen.getByTestId('playbook-title')).toHaveTextContent('RULES.md'));
    expect(screen.getByLabelText('Lines: 2')).toBeTruthy();
    expect(screen.getByLabelText('Headings: 1')).toBeTruthy();
    // The owner may edit a personal playbook.
    expect(screen.getByTestId('playbook-edit')).toBeTruthy();
  });

  it('skills/new creates a personal skill for a regular user', async () => {
    api.createSkill.mockResolvedValue({ id: 'skl_1', name: 'TDD' } as never);
    api.getSkill.mockResolvedValue({ id: 'skl_1', tenantId: 'ten_1', userId: 'usr_1', scope: 'UserSpecific', name: 'TDD', description: null, category: null, content: 'x', isBuiltIn: false, active: true, ...STAMP });
    const h = await renderW4Routes(ROUTES, '/skills/new', 'user');
    await waitFor(() => expect(screen.getByTestId('skill-form-name')).toBeTruthy());
    await fireEvent.changeText(screen.getByTestId('skill-form-name'), 'TDD');
    await fireEvent.changeText(screen.getByTestId('skill-form-content'), 'x');
    await act(async () => { await fireEvent.press(screen.getByTestId('skill-form-submit')); });
    expect(api.createSkill).toHaveBeenCalledWith({ name: 'TDD', description: null, category: null, content: 'x', active: true, scope: 'UserSpecific' });
    await waitFor(() => expect(h.getPathname()).toBe('/skills/skl_1'));
  });
});

describe('endpoints, harbors, memories', () => {
  const ENDPOINT = { id: 'mep_1', tenantId: 'ten_1', userId: null, scope: 'TenantWide', name: 'Embed', kind: 'Embedding', provider: 'OpenAI', baseUrl: 'https://api', model: 'm', hasApiKey: true, healthStatus: 'Healthy', healthHistory: [{ timestampUtc: 'x', success: true }], uptimePercentage: 100, consecutiveSuccesses: 3, consecutiveFailures: 0, enabled: true, dimensionality: 3, timeoutMs: 1000, lastHealthCheckUtc: null, firstHealthCheckUtc: null, lastHealthyUtc: null, lastUnhealthyUtc: null, lastHealthError: null, lastLatencyMs: 12, region: null, project: null, apiVersion: null, accessKeyId: null, ...STAMP } as ModelEndpoint;

  it('endpoints: health sweep, validate from the detail, and the key is never shown', async () => {
    api.listModelEndpoints.mockResolvedValue([ENDPOINT]);
    api.getModelEndpoint.mockResolvedValue(ENDPOINT);
    api.healthCheckModelEndpoints.mockResolvedValue({ distinctBaseUrlsProbed: 1 });
    api.validateModelEndpoint.mockResolvedValue({ success: true, baseUrl: 'https://api', latencyMs: 40, statusCode: 200, error: null, embeddingDimensions: 3, sampleText: null, timestampUtc: 'x' });
    await renderW4Routes(ROUTES, '/configuration?tab=endpoints');
    await waitFor(() => expect(screen.getByTestId('endpoint-row-mep_1')).toBeTruthy());
    await act(async () => { await fireEvent.press(screen.getByTestId('endpoints-sweep')); });
    expect(api.healthCheckModelEndpoints).toHaveBeenCalled();
    await fireEvent.press(screen.getByTestId('endpoint-row-mep_1'));
    await waitFor(() => expect(screen.getByTestId('endpoint-validate')).toBeTruthy());
    expect(screen.getByText('Stored')).toBeTruthy();
    await act(async () => { await fireEvent.press(screen.getByTestId('endpoint-validate')); });
    expect(api.validateModelEndpoint).toHaveBeenCalledWith('mep_1');
    await waitFor(() => expect(screen.getByText('40 ms')).toBeTruthy());
  });

  it('harbors: enable / disable, and regular users cannot register', async () => {
    const harbor: Harbor = { id: 'hbr_1', tenantId: 'ten_1', userId: null, name: 'Laptop', capabilities: [{ name: 'git', available: true, detail: null }], connectionStatus: 'Connected', maxConcurrentJobs: 4, enabled: true, protocolVersion: '1', osPlatform: 'macOS', architecture: 'arm64', lastSeenUtc: null, lastConnectedUtc: null, ...STAMP };
    api.listHarbors.mockResolvedValue([harbor]);
    api.disableHarbor.mockResolvedValue({ ...harbor, enabled: false });
    await renderW4Routes(ROUTES, '/configuration?tab=harbors');
    await waitFor(() => expect(screen.getByTestId('harbor-row-hbr_1')).toBeTruthy());
    await act(async () => { await fireEvent(rowActionTarget(screen.getByTestId('harbor-row-hbr_1-swipe'), 'toggle'), 'accessibilityAction', { nativeEvent: { actionName: 'toggle' } }); });
    expect(api.disableHarbor).toHaveBeenCalledWith('hbr_1');
  });

  it('harbors are read-only for regular users', async () => {
    api.listHarbors.mockResolvedValue([]);
    await renderW4Routes(ROUTES, '/configuration?tab=harbors', 'user');
    await waitFor(() => expect(screen.getByText('No harbors match the current filters.')).toBeTruthy());
    expect(screen.queryByTestId('harbors-create')).toBeNull();
  });

  it('memories load from the server and filter by type', async () => {
    const memory = (id: string): Memory => ({ id, scope: 'TenantWide', tenantId: 'ten_1', userId: null, type: 'Semantic', topic: 'db', summary: `Summary ${id}`, content: 'c', salience: 0.5, version: 1, sourceKind: 'Voyage', tags: [], ...STAMP });
    api.listMemories.mockImplementation(async (params) => (params?.pageNumber === 2
      ? page([memory('mem_2')], { totalPages: 2, pageNumber: 2 })
      : page([memory('mem_1')], { totalPages: 2 })) as never);
    await renderW4Routes(ROUTES, '/configuration?tab=memory');
    await waitFor(() => expect(screen.getByText('Summary mem_1')).toBeTruthy());
    expect(api.listMemories).toHaveBeenCalledWith({ pageNumber: 1, pageSize: 25, filters: {} });
    // The type filter is a server filter: page 1 is reloaded with it.
    await fireEvent.press(screen.getByTestId('memories-filters'));
    await fireEvent.press(screen.getByTestId('memories-filter-type'));
    await fireEvent.press(screen.getByTestId('memories-filter-type-option-Procedural'));
    await waitFor(() => expect(api.listMemories).toHaveBeenLastCalledWith({ pageNumber: 1, pageSize: 25, filters: { type: 'Procedural' } }));
  });
});
