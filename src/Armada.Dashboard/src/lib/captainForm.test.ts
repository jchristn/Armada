import { describe, expect, it } from 'vitest';
import type { Captain } from '../types/models';
import {
  CAPTAIN_RUNTIMES,
  CAPTAIN_RUNTIME_TOOLTIP,
  buildCaptainCreatePayload,
  buildCaptainPayload,
  captainDetailActions,
  captainFormError,
  captainFormErrorMessage,
  captainFormFromCaptain,
  cliPolicyChanged,
  emptyCaptainForm,
} from './captainForm';

function captain(over: Partial<Captain> = {}): Captain {
  return {
    id: 'cpt_1', tenantId: null, name: 'Ada', runtime: 'ClaudeCode', supportsPlanningSessions: true, planningSessionSupportReason: null,
    systemInstructions: null, model: null, allowedPersonas: null, preferredPersona: null, state: 'Idle', currentMissionId: null,
    currentDockId: null, processId: null, recoveryAttempts: 0, lastHeartbeatUtc: null, createdUtc: '2026-01-01T00:00:00Z', lastUpdateUtc: '2026-01-01T00:00:00Z',
    ...over,
  };
}

describe('captainForm', () => {
  it('defaults Active for older captains and preserves explicit false in both payloads', () => {
    expect(emptyCaptainForm().active).toBe(true);
    expect(captainFormFromCaptain(captain()).active).toBe(true);
    const form = captainFormFromCaptain(captain({ active: false }));
    expect(form.active).toBe(false);
    expect(buildCaptainPayload(form).active).toBe(false);
    expect(buildCaptainCreatePayload(form).active).toBe(false);
  });
  it('builds the create payload like the Captains page did', () => {
    const form = { ...emptyCaptainForm(), name: 'Ada', runtime: 'Codex', model: '  gpt-5 ', autoApprove: false, cliPermissionPolicy: 'Refuse' as const };
    expect(buildCaptainCreatePayload(form)).toEqual({
      active: true, name: 'Ada', runtime: 'Codex', model: 'gpt-5', modelEndpointId: null, reasoningEffort: null, tier: null,
      runtimeOptionsJson: '{"autoApprove":false}', cliPermissionPolicy: 'Refuse',
    });
    expect(buildCaptainPayload(form)).not.toHaveProperty('cliPermissionPolicy');
  });

  it('keeps the endpoint only for API-endpoint captains and drops absent persona fields', () => {
    const form = { ...emptyCaptainForm(), name: 'Api', runtime: 'ApiEndpoint', modelEndpointId: 'mep_1', systemInstructions: 'be brief', tier: 'Premium' };
    const payload = buildCaptainPayload(form);
    expect(payload).toMatchObject({ modelEndpointId: 'mep_1', systemInstructions: 'be brief', tier: 'Premium', runtimeOptionsJson: null });
    expect(payload).not.toHaveProperty('allowedPersonas');
    expect(payload).not.toHaveProperty('muxEndpoint');
    expect(payload).not.toHaveProperty('autoApprove');
  });

  it('builds the detail edit payload with personas and no endpoint key', () => {
    const form = captainFormFromCaptain(captain({ runtime: '', allowedPersonas: '["Worker"]', modelEndpointId: 'mep_1' }), { withPersonas: true, defaultRuntime: 'ClaudeCode' });
    expect(form.runtime).toBe('ClaudeCode');
    const payload = buildCaptainPayload(form);
    expect(payload).toMatchObject({ allowedPersonas: '["Worker"]', preferredPersona: null, systemInstructions: null });
    expect(payload).not.toHaveProperty('modelEndpointId');
  });

  it('keeps the persona fields and endpoint of a captain edited from the list', () => {
    const form = captainFormFromCaptain(captain({ runtime: 'ApiEndpoint', allowedPersonas: '["Worker"]', preferredPersona: 'Worker', modelEndpointId: 'mep_1' }), { withEndpoint: true, withPersonas: true });
    expect(buildCaptainPayload(form)).toMatchObject({ allowedPersonas: '["Worker"]', preferredPersona: 'Worker', modelEndpointId: 'mep_1' });
    const cleared = buildCaptainPayload({ ...form, allowedPersonas: '', preferredPersona: ' ', systemInstructions: '' });
    expect(cleared).toMatchObject({ allowedPersonas: null, preferredPersona: null, systemInstructions: null });
    expect(buildCaptainCreatePayload({ ...form, allowedPersonas: '', preferredPersona: '' })).not.toHaveProperty('allowedPersonas');
  });

  it('rejects invalid Mux temperature and max tokens instead of saving null', () => {
    const mux = { ...emptyCaptainForm(), name: 'M', runtime: 'Mux', muxEndpoint: 'local' };
    expect(captainFormError({ ...mux, muxTemperature: 'warm' })).toBe('muxTemperatureInvalid');
    expect(captainFormError({ ...mux, muxTemperature: '-1' })).toBe('muxTemperatureInvalid');
    expect(captainFormError({ ...mux, muxTemperature: '0.2' })).toBeNull();
    expect(captainFormError({ ...mux, muxMaxTokens: '12abc' })).toBe('muxMaxTokensInvalid');
    expect(captainFormError({ ...mux, muxMaxTokens: '1.5' })).toBe('muxMaxTokensInvalid');
    expect(captainFormError({ ...mux, muxMaxTokens: '0' })).toBe('muxMaxTokensInvalid');
    expect(captainFormError({ ...mux, muxMaxTokens: '8192' })).toBeNull();
    expect(captainFormErrorMessage('muxMaxTokensInvalid')).toContain('Max Tokens');
    expect(captainFormError({ ...emptyCaptainForm(), runtime: 'Codex', muxTemperature: 'warm' })).toBeNull();
  });

  it('saves the unsupported mux approval policy ask as deny', () => {
    const mux = { ...emptyCaptainForm(), name: 'M', runtime: 'Mux', muxEndpoint: 'local', muxApprovalPolicy: 'ask' };
    expect(JSON.parse(buildCaptainPayload(mux).runtimeOptionsJson as string)).toMatchObject({ approvalPolicy: 'deny' });
    expect(JSON.parse(buildCaptainPayload({ ...mux, muxApprovalPolicy: 'auto' }).runtimeOptionsJson as string)).toMatchObject({ approvalPolicy: 'auto' });
  });

  it('writes mux options and validates required fields', () => {
    const mux = { ...emptyCaptainForm(), name: 'M', runtime: 'Mux' };
    expect(captainFormError(mux)).toBe('muxEndpointRequired');
    const ok = { ...mux, muxEndpoint: 'local', muxMaxTokens: '200' };
    expect(captainFormError(ok)).toBeNull();
    expect(JSON.parse(buildCaptainPayload(ok).runtimeOptionsJson as string)).toMatchObject({ endpoint: 'local', maxTokens: 200 });
    expect(captainFormError({ ...emptyCaptainForm(), runtime: 'ApiEndpoint' })).toBe('inferenceEndpointRequired');
    const detail = captainFormFromCaptain(captain({ runtime: 'ApiEndpoint' }), { withPersonas: true });
    expect(captainFormError(detail)).toBeNull();
  });

  it('round trips capacity and leaves blank creation to the server runtime default', () => {
    expect(buildCaptainCreatePayload({ ...emptyCaptainForm(), name: 'new', runtime: 'Mux' })).not.toHaveProperty('maxConcurrency');
    const form = captainFormFromCaptain(captain({ maxConcurrency: 3 }));
    expect(form.maxConcurrency).toBe('3');
    expect(buildCaptainPayload(form).maxConcurrency).toBe(3);
    expect(buildCaptainPayload({ ...form, maxConcurrency: '0' }).maxConcurrency).toBe(1);
    expect(buildCaptainPayload({ ...form, maxConcurrency: '300' }).maxConcurrency).toBe(256);
    expect(buildCaptainPayload({ ...form, maxConcurrency: '2.9' }).maxConcurrency).toBe(2);
  });

  it('detects CLI policy changes and lifecycle actions', () => {
    expect(cliPolicyChanged(captain(), { cliPermissionPolicy: null })).toBe(false);
    expect(cliPolicyChanged(captain({ cliPermissionPolicy: 'Bypass' }), { cliPermissionPolicy: null })).toBe(true);
    expect(captainDetailActions('Working')).toEqual({ recall: true, stop: true });
    expect(captainDetailActions('Planning')).toEqual({ recall: false, stop: true });
    expect(captainDetailActions('Idle')).toEqual({ recall: false, stop: false });
  });

  it('offers pi as a runtime and names it in the runtime tooltip', () => {
    expect(CAPTAIN_RUNTIMES).toContainEqual({ value: 'Pi', label: 'pi' });
    expect(CAPTAIN_RUNTIMES.findIndex(r => r.value === 'Pi')).toBe(CAPTAIN_RUNTIMES.findIndex(r => r.value === 'OpenCode') + 1);
    expect(CAPTAIN_RUNTIME_TOOLTIP).toContain('OpenCode, pi)');
    const form = { ...emptyCaptainForm(), name: 'pi-1', runtime: 'Pi', autoApprove: false };
    expect(buildCaptainCreatePayload(form)).toMatchObject({ name: 'pi-1', runtime: 'Pi', modelEndpointId: null, runtimeOptionsJson: '{"autoApprove":false}' });
  });
});
