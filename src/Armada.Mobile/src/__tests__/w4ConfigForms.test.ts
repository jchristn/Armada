import type { ModelEndpoint, WorkflowProfile } from '@dashboard/types/models';
import type { ScopeViewer } from '@dashboard/lib/scoping';
import { countProfileCapabilities, formatHealthSpan, formatStages, splitList, unsupportedEndpointReason } from '@dashboard/lib/configuration';
import { scopeValue } from '../screens/configuration/common';
import { endpointPayload, endpointValues } from '../screens/configuration/endpointForm';
import { memoryFilters, memoryPayload, memoryValues } from '../screens/configuration/MemoriesTab';
import { moveStage, stageEntries, stagesPayload } from '../screens/configuration/pipelineForm';
import { applyWorkflowValues, blankWorkflowProfile, projectPayload, projectValues, blankProjectProfile, workflowPayload, workflowValues } from '../screens/configuration/profileForms';
import { playbookStats } from '../screens/configuration/simpleForms';

const ADMIN: ScopeViewer = { isAdmin: false, isTenantAdmin: true, tenantId: 'ten_1', userId: 'usr_1' };
const USER: ScopeViewer = { isAdmin: false, isTenantAdmin: false, tenantId: 'ten_1', userId: 'usr_1' };

describe('configuration form logic', () => {
  it('scope: admins choose, users create personal and keep the record scope on edit', () => {
    expect(scopeValue(ADMIN, { scope: 'UserSpecific' })).toBe('UserSpecific');
    expect(scopeValue(ADMIN, {})).toBe('TenantWide');
    expect(scopeValue(USER, { scope: 'TenantWide' })).toBe('UserSpecific');
    expect(scopeValue(USER, { scope: 'UserSpecific' }, 'TenantWide')).toBe('TenantWide');
  });

  it('pipeline stages: blanks dropped, order renumbered, moves bounded', () => {
    const stages = stageEntries([
      { id: 's2', pipelineId: 'p', order: 2, personaName: 'Worker', isOptional: false, description: null, requiresReview: true, reviewDenyAction: 'FailPipeline' },
      { id: 's1', pipelineId: 'p', order: 1, personaName: 'Architect', isOptional: true, description: 'Plan', requiresReview: false, reviewDenyAction: 'RetryStage' },
    ]);
    expect(stages.map((s) => s.personaName)).toEqual(['Architect', 'Worker']);
    const moved = moveStage(stages, 1, -1);
    expect(moved.map((s) => s.personaName)).toEqual(['Worker', 'Architect']);
    expect(moveStage(stages, 0, -1)).toBe(stages);
    expect(stagesPayload([...moved, { ...moved[0], personaName: '  ' }])).toEqual([
      { personaName: 'Worker', isOptional: false, description: null, requiresReview: true, reviewDenyAction: 'FailPipeline', order: 1 },
      { personaName: 'Architect', isOptional: true, description: 'Plan', requiresReview: false, reviewDenyAction: 'RetryStage', order: 2 },
    ]);
    expect(formatStages([])).toBe('-');
  });

  it('workflow profiles: the editor payload trims, nulls blanks, and keeps only the scope target', () => {
    const base = blankWorkflowProfile(ADMIN, { scope: 'Vessel', vesselId: 'vsl_1' });
    expect(base.scope).toBe('Vessel');
    const v = { ...workflowValues(base), name: '  Build  ', buildCommand: '  dotnet build ', lintCommand: '  ', fleetId: 'flt_1', languageHints: 'dotnet, react' };
    const next = applyWorkflowValues(ADMIN, base, v, false);
    next.environments = [{ environmentName: ' prod ', deployCommand: ' deploy ', rollbackCommand: '', smokeTestCommand: null, healthCheckCommand: null, deploymentVerificationCommand: null, rollbackVerificationCommand: null }, { environmentName: ' ', deployCommand: null, rollbackCommand: null, smokeTestCommand: null, healthCheckCommand: null, deploymentVerificationCommand: null, rollbackVerificationCommand: null }];
    next.requiredInputs = [{ provider: 'EnvironmentVariable', key: ' TOKEN ', environmentName: '', description: '' }, { provider: 'FilePath', key: '  ' }];
    const payload = workflowPayload(next);
    expect(payload).toMatchObject({ name: 'Build', buildCommand: 'dotnet build', lintCommand: null, vesselId: 'vsl_1', fleetId: null, languageHints: ['dotnet', 'react'], ownershipScope: 'TenantWide' });
    expect(payload.environments).toEqual([{ environmentName: 'prod', deployCommand: 'deploy', rollbackCommand: null, smokeTestCommand: null, healthCheckCommand: null, deploymentVerificationCommand: null, rollbackVerificationCommand: null }]);
    expect(payload.requiredInputs).toEqual([{ provider: 'EnvironmentVariable', key: 'TOKEN', environmentName: null, description: null }]);
    expect(countProfileCapabilities({ ...base, buildCommand: 'b', environments: [{ environmentName: 'p', deployCommand: 'd', rollbackCommand: null, smokeTestCommand: 's', healthCheckCommand: null, deploymentVerificationCommand: 'x', rollbackVerificationCommand: null }] } as WorkflowProfile)).toBe(3);
  });

  it('project profiles: skills split and overrides kept', () => {
    const values = { ...projectValues(blankProjectProfile(USER)), scope: 'Fleet', fleetId: 'flt_1', vesselId: 'vsl_9', skills: 'dotnet\ntdd, perf' };
    expect(projectPayload(USER, values, [{ personaName: ' Worker ', promptTemplateName: '', additionalInstructions: 'x', enabled: true }], null)).toMatchObject({
      scope: 'Fleet', fleetId: 'flt_1', vesselId: null, skills: ['dotnet', 'tdd', 'perf'], ownershipScope: 'UserSpecific',
      personaOverrides: [{ personaName: 'Worker', promptTemplateName: null, additionalInstructions: 'x', enabled: true }],
    });
    expect(splitList(' a,\nb , ')).toEqual(['a', 'b']);
  });

  it('endpoints: the credential is sent only when typed; provider guard', () => {
    const existing = { id: 'mep_1', scope: 'TenantWide', name: 'E', kind: 'Embedding', provider: 'OpenAI', baseUrl: 'https://x', hasApiKey: true, dimensionality: 3, timeoutMs: 1000, enabled: true } as ModelEndpoint;
    const v = endpointValues(ADMIN, existing);
    expect(v.apiKey).toBe('');
    expect(endpointPayload(ADMIN, v, existing)).not.toHaveProperty('apiKey');
    expect(endpointPayload(ADMIN, { ...v, apiKey: 'sk-new' }, existing).apiKey).toBe('sk-new');
    expect(unsupportedEndpointReason('Anthropic', 'Embedding')).toMatch(/embeddings API/);
    expect(unsupportedEndpointReason('OpenAI', 'Embedding')).toBeNull();
    expect(formatHealthSpan('2026-10-01T00:00:00Z', Date.parse('2026-10-02T03:04:00Z'))).toBe('1d 3h');
    expect(formatHealthSpan(null)).toBe('-');
  });

  it('memories and playbooks', () => {
    expect(memoryFilters('Semantic', '  db ')).toEqual({ type: 'Semantic', search: 'db' });
    expect(memoryFilters('', ' ')).toEqual({});
    expect(memoryPayload(ADMIN, { ...memoryValues(ADMIN, null), content: 'c', salience: 'x', tags: 'a, b' }, null)).toMatchObject({ salience: 0.5, tags: ['a', 'b'], topic: null, vesselId: null, scope: 'TenantWide' });
    expect(playbookStats('# A\ntext\n## B')).toEqual({ characters: 13, lines: 3, headings: 2 });
    expect(playbookStats('')).toEqual({ characters: 0, lines: 0, headings: 0 });
  });
});
