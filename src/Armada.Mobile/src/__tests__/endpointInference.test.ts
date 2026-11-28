import { endpointValues, endpointPayload, endpointValidate, endpointFields } from '../screens/configuration/endpointForm';
import type { ModelEndpoint } from '@dashboard/types/models';
import { defaultInferenceOptions } from '@dashboard/lib/inferenceOptions';

const viewer = { isAdmin: true, isTenantAdmin: true, tenantId: 'ten_1', userId: 'usr_1' };
const t = (text: string) => text;

test('mobile exposes inference controls and carries exact policy and zero temperature', () => {
  const values = { ...endpointValues(viewer, null), allowedTools: 'read_file\ngrep', readOnlyTools: true, requireHarbor: true, mcpToolsEnabled: false, maxIterations: '7', maxTokens: '512', temperature: '0', topP: '1' };
  expect(endpointValidate(t, values, null)).toBeNull();
  expect(endpointPayload(viewer, values, null).inferenceOptions).toEqual({ ...defaultInferenceOptions, streamingMode: 'Automatic', allowedTools: ['read_file', 'grep'], readOnlyTools: true, requireHarbor: true, mcpToolsEnabled: false, maxIterations: 7, maxTokens: 512, temperature: 0, topP: 1 });
  expect(endpointFields(t, viewer, values, null).map(field => field.key)).toEqual(expect.arrayContaining(['toolsEnabled', 'readOnlyTools', 'mcpToolsEnabled', 'requireHarbor', 'maxIterations', 'maxTokens', 'temperature', 'topP', 'allowedTools']));
});

test('editing an empty allowlist cannot accidentally enable all tools', () => {
  const endpoint = { inferenceOptions: { ...defaultInferenceOptions, allowedTools: [] }, headers: [] } as unknown as ModelEndpoint;
  const values = endpointValues(viewer, endpoint);
  expect(endpointPayload(viewer, values, endpoint).inferenceOptions?.toolsEnabled).toBe(false);
});

test.each<Record<string, string>>([{ maxIterations: '0' }, { maxIterations: '1.5' }, { temperature: 'oops' }, { topP: '2' }, { allowedTools: 'read_file\nread_file' }])('mobile rejects invalid inference options %o', patch => {
  expect(endpointValidate(t, { ...endpointValues(viewer, null), ...patch }, null)).not.toBeNull();
});

test('editing an endpoint keeps streaming, the context window, the concurrency cap and the first-response timeout', () => {
  const stored = { ...defaultInferenceOptions, streaming: true, contextLength: 32768, maxConcurrentMissions: 2, firstResponseTimeoutMs: 300000 };
  const endpoint = { kind: 'Inference', inferenceOptions: stored, headers: [] } as unknown as ModelEndpoint;
  const values = endpointValues(viewer, endpoint);
  expect(endpointPayload(viewer, values, endpoint).inferenceOptions).toEqual(expect.objectContaining({ streamingMode: 'On', streaming: true, contextLength: 32768, maxConcurrentMissions: 2, firstResponseTimeoutMs: 300000 }));
  expect(endpointFields(t, viewer, values, endpoint).map(field => field.key)).toEqual(expect.arrayContaining(['streamingMode', 'firstResponseTimeoutMs', 'contextLength', 'maxConcurrentMissions']));
  expect(endpointValidate(t, { ...values, contextLength: '100' }, endpoint)).toContain('Context window');
});

test('streaming mode: Automatic by default, an explicit Off is kept, and only On sends the compatibility flag', () => {
  const fresh = endpointPayload(viewer, endpointValues(viewer, null), null).inferenceOptions;
  expect(fresh).toEqual(expect.objectContaining({ streamingMode: 'Automatic', streaming: false }));
  const off = { kind: 'Inference', inferenceOptions: { ...defaultInferenceOptions, streamingMode: 'Off' }, headers: [] } as unknown as ModelEndpoint;
  expect(endpointPayload(viewer, endpointValues(viewer, off), off).inferenceOptions).toEqual(expect.objectContaining({ streamingMode: 'Off', streaming: false }));
  const legacy = { kind: 'Inference', inferenceOptions: { ...defaultInferenceOptions, streaming: false }, headers: [] } as unknown as ModelEndpoint;
  expect(endpointPayload(viewer, endpointValues(viewer, legacy), legacy).inferenceOptions).toEqual(expect.objectContaining({ streamingMode: 'Automatic' }));
});
