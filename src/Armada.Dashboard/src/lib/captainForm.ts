import type { Captain, CliPermissionPolicy } from '../types/models';
import { applyAutoApprove, autoApproveFromCaptain, supportsAutoApproveSwitch } from './captainApproval';
import { buildMuxRuntimeOptionsJson, EMPTY_MUX_CAPTAIN_FORM, isMuxRuntime, muxFormFromCaptain, type MuxCaptainFormFields } from './mux';

/**
 * Captain create / edit form shared by the dashboard (Captains list modal, captain detail modal) and the mobile app.
 * `modelEndpointId` (API-endpoint captains) and the persona fields are optional: a form without the key leaves the
 * value out of the payload, exactly as the page without that field did.
 */
export type CaptainFormState = {
  name: string;
  runtime: string;
  systemInstructions: string;
  model: string;
  modelEndpointId?: string;
  reasoningEffort: string;
  tier: string;
  allowedPersonas?: string;
  preferredPersona?: string;
  autoApprove: boolean;
  /** CLI tool permission policy; null inherits. Saved through its own endpoint on edit. */
  cliPermissionPolicy: CliPermissionPolicy | null;
} & MuxCaptainFormFields;

/** Runtimes offered when creating a captain (value, display label). */
export const CAPTAIN_RUNTIMES: { value: string; label: string }[] = [
  { value: 'ClaudeCode', label: 'Claude Code' },
  { value: 'Codex', label: 'Codex' },
  { value: 'Gemini', label: 'Gemini' },
  { value: 'Cursor', label: 'Cursor' },
  { value: 'Mux', label: 'Mux' },
  { value: 'OpenCode', label: 'OpenCode' },
  { value: 'ApiEndpoint', label: 'API Endpoint' },
];

export const REASONING_EFFORTS = ['Off', 'Minimal', 'Low', 'Medium', 'High'];
export const CAPTAIN_TIERS = ['Economy', 'Standard', 'Premium'];

/** An empty create form (auto-approve on, CLI policy inherited). */
export function emptyCaptainForm(): CaptainFormState {
  return { name: '', runtime: '', systemInstructions: '', model: '', modelEndpointId: '', reasoningEffort: '', tier: '', autoApprove: true, cliPermissionPolicy: null, ...EMPTY_MUX_CAPTAIN_FORM };
}

/** The form for editing a captain. `withPersonas` adds the persona fields (the detail page's form). */
export function captainFormFromCaptain(c: Captain, options: { withEndpoint?: boolean; withPersonas?: boolean; defaultRuntime?: string } = {}): CaptainFormState {
  const form: CaptainFormState = {
    name: c.name,
    runtime: c.runtime || options.defaultRuntime || '',
    systemInstructions: c.systemInstructions ?? '',
    model: c.model ?? '',
    reasoningEffort: c.reasoningEffort ?? '',
    tier: c.tier ?? '',
    autoApprove: autoApproveFromCaptain(c),
    cliPermissionPolicy: c.cliPermissionPolicy ?? null,
    ...muxFormFromCaptain(c),
  };
  if (options.withEndpoint) form.modelEndpointId = c.modelEndpointId ?? '';
  if (options.withPersonas) {
    form.allowedPersonas = c.allowedPersonas ?? '';
    form.preferredPersona = c.preferredPersona ?? '';
  }
  return form;
}

/** Why a captain form cannot be saved (the caller translates the message), or null when it can. */
export type CaptainFormError = 'muxEndpointRequired' | 'inferenceEndpointRequired';

export function captainFormError(form: CaptainFormState): CaptainFormError | null {
  if (isMuxRuntime(form.runtime) && !form.muxEndpoint.trim()) return 'muxEndpointRequired';
  if ('modelEndpointId' in form && form.runtime === 'ApiEndpoint' && !form.modelEndpointId) return 'inferenceEndpointRequired';
  return null;
}

/** English message of a form error (pass it through the translator). */
export function captainFormErrorMessage(error: CaptainFormError): string {
  return error === 'muxEndpointRequired'
    ? 'Mux captains require a named Mux endpoint.'
    : 'API-endpoint captains require an inference endpoint. Select one, or add it under Configuration > Endpoints.';
}

/**
 * The create / update request body. The CLI tool permission policy is never part of it (it changes through its own
 * admin endpoint on edit; `buildCaptainCreatePayload` adds it on create).
 */
export function buildCaptainPayload(form: CaptainFormState): Record<string, unknown> {
  const payload = { ...form } as Record<string, unknown>;
  if (!payload.systemInstructions) delete payload.systemInstructions;
  payload.model = form.model.trim() ? form.model.trim() : null;
  if ('modelEndpointId' in form) payload.modelEndpointId = form.runtime === 'ApiEndpoint' ? (form.modelEndpointId || null) : null;
  payload.reasoningEffort = form.reasoningEffort ? form.reasoningEffort : null;
  payload.tier = form.tier ? form.tier : null;
  if ('allowedPersonas' in form && !payload.allowedPersonas) delete payload.allowedPersonas;
  if ('preferredPersona' in form && !payload.preferredPersona) delete payload.preferredPersona;
  payload.runtimeOptionsJson = applyAutoApprove(buildMuxRuntimeOptionsJson(form.runtime, form), form.autoApprove || !supportsAutoApproveSwitch(form.runtime));
  delete payload.autoApprove;
  delete payload.muxConfigDirectory;
  delete payload.muxEndpoint;
  delete payload.muxBaseUrl;
  delete payload.muxAdapterType;
  delete payload.muxTemperature;
  delete payload.muxMaxTokens;
  delete payload.muxSystemPromptPath;
  delete payload.muxApprovalPolicy;
  delete payload.cliPermissionPolicy;
  return payload;
}

/** The create request body: the update body plus the CLI tool permission policy when one is chosen. */
export function buildCaptainCreatePayload(form: CaptainFormState): Record<string, unknown> {
  const payload = buildCaptainPayload(form);
  if (form.cliPermissionPolicy) payload.cliPermissionPolicy = form.cliPermissionPolicy;
  return payload;
}

/** True when an edit changed the CLI tool permission policy (saved through setCaptainCliPermissionPolicy). */
export function cliPolicyChanged(captain: Pick<Captain, 'cliPermissionPolicy'>, form: Pick<CaptainFormState, 'cliPermissionPolicy'>): boolean {
  return (captain.cliPermissionPolicy ?? null) !== form.cliPermissionPolicy;
}

/** Captain states that can be recalled or stopped from the detail page (Planning can only be stopped). */
export function captainDetailActions(state: string | null | undefined): { recall: boolean; stop: boolean } {
  const working = state === 'Working' || state === 'Stalled';
  return { recall: working, stop: working || state === 'Planning' };
}
