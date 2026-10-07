import { describe, expect, it } from 'vitest';
import type { Captain } from '../types/models';
import {
  buildCaptainCreatePayload,
  buildCaptainPayload,
  captainDetailActions,
  captainFormError,
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
  it('builds the create payload like the Captains page did', () => {
    const form = { ...emptyCaptainForm(), name: 'Ada', runtime: 'Codex', model: '  gpt-5 ', autoApprove: false, cliPermissionPolicy: 'Refuse' as const };
    expect(buildCaptainCreatePayload(form)).toEqual({
      name: 'Ada', runtime: 'Codex', model: 'gpt-5', modelEndpointId: null, reasoningEffort: null, tier: null,
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
    expect(payload).toMatchObject({ allowedPersonas: '["Worker"]' });
    expect(payload).not.toHaveProperty('preferredPersona');
    expect(payload).not.toHaveProperty('modelEndpointId');
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

  it('detects CLI policy changes and lifecycle actions', () => {
    expect(cliPolicyChanged(captain(), { cliPermissionPolicy: null })).toBe(false);
    expect(cliPolicyChanged(captain({ cliPermissionPolicy: 'Bypass' }), { cliPermissionPolicy: null })).toBe(true);
    expect(captainDetailActions('Working')).toEqual({ recall: true, stop: true });
    expect(captainDetailActions('Planning')).toEqual({ recall: false, stop: true });
    expect(captainDetailActions('Idle')).toEqual({ recall: false, stop: false });
  });
});
