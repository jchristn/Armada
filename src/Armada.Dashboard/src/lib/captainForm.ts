import type { Captain, CliPermissionPolicy } from '../types/models';
import { applyAutoApprove, autoApproveFromCaptain, supportsAutoApproveSwitch } from './captainApproval';
import { buildMuxRuntimeOptionsJson, EMPTY_MUX_CAPTAIN_FORM, isMuxRuntime, muxFieldErrorMessage, muxFormFieldError, muxFormFromCaptain, type MuxCaptainFormFields, type MuxFieldError } from './mux';

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
  active?: boolean;
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
  { value: 'Pi', label: 'pi' },
  { value: 'ApiEndpoint', label: 'API Endpoint' },
];

/** English tooltip for the captain Model field (translate with t()); what the value means depends on the runtime. */
export const CAPTAIN_MODEL_TOOLTIP = 'Optional model name passed to the runtime, for example sonnet or gpt-5.4-mini. Leave blank to use the runtime\'s default model. Also used to guess the tier when Tier is Auto.';
/** Mux: the model is a --model override, not the endpoint name (that is the Mux Endpoint field). */
export const CAPTAIN_MODEL_TOOLTIP_MUX = 'Optional model passed to mux as --model, overriding the model saved on the mux endpoint (for example gpt-oss:20b). Put the endpoint name in Mux Endpoint, not here. Blank uses the endpoint\'s model.';
/** API endpoint captains: the model overrides the one configured on the selected inference endpoint. */
export const CAPTAIN_MODEL_TOOLTIP_API_ENDPOINT = 'Optional model name that overrides the model set on the chosen inference endpoint, for example gpt-5.4-mini. Choose the endpoint in Inference Endpoint. Blank uses the endpoint\'s model.';

/** English tooltip for the captain Name field. */
export const CAPTAIN_NAME_TOOLTIP = 'Display name for this captain, shown in lists, logs and mission assignments, for example claude-1 or codex-reviewer. Required.';
/** English tooltip for the captain Runtime field. */
export const CAPTAIN_RUNTIME_TOOLTIP = 'The agent program this captain runs. CLI runtimes (Claude Code, Codex, Gemini, Cursor, Mux, OpenCode, pi) must be installed where captains run; API Endpoint calls a configured model endpoint directly. Required.';
/** English tooltip for the captain Inference Endpoint field (API endpoint captains). */
export const CAPTAIN_INFERENCE_ENDPOINT_TOOLTIP = 'Required for API Endpoint captains: the inference endpoint (provider, URL and model) this captain sends its requests to. Add or edit endpoints under Configuration > Endpoints.';
/** English tooltip for the captain Reasoning effort field. */
export const CAPTAIN_REASONING_EFFORT_TOOLTIP = 'How much the model reasons before answering, mapped to the runtime\'s own control (Claude Code thinking budget, Codex reasoning effort, Mux --effort). Higher is slower and costs more. Runtime default sends nothing.';
/** English tooltip for the captain Capability tier field. */
export const CAPTAIN_TIER_TOOLTIP = 'Cost and capability class dispatch uses to match missions to captains: Economy for routine work, Standard for general work, Premium for complex work. Auto guesses it from the model name (Standard if unknown).';
/** English tooltip for the captain auto-approve switch. */
export const CAPTAIN_AUTO_APPROVE_TOOLTIP = 'On: the CLI runs with its permission-bypass flag, so its shell commands and edits run without asking. Off: it runs in its safer mode and commands that need approval are refused.';
/** English tooltip for the captain CLI tool permissions field. */
export const CAPTAIN_CLI_POLICY_TOOLTIP = 'How this captain\'s own shell, web and file tools are allowed: Refuse, Approve in Armada (a person approves each request), Bypass (admins only), or Inherit the auto-approve switch and server default.';
/** English tooltip for the captain System Instructions field. */
export const CAPTAIN_SYSTEM_INSTRUCTIONS_TOOLTIP = 'Optional text added to every mission prompt this captain gets, before the vessel context and mission details. Use it to specialize behavior or add guardrails. Blank adds nothing.';

/** English tooltip for the Model field for a runtime. */
export function captainModelTooltip(runtime: string | null | undefined): string {
  if (isMuxRuntime(runtime)) return CAPTAIN_MODEL_TOOLTIP_MUX;
  if (runtime === 'ApiEndpoint') return CAPTAIN_MODEL_TOOLTIP_API_ENDPOINT;
  return CAPTAIN_MODEL_TOOLTIP;
}

/** English placeholder for the Model field for a runtime. */
export function captainModelPlaceholder(runtime: string | null | undefined): string {
  if (isMuxRuntime(runtime)) return 'Optional; overrides the mux endpoint model';
  if (runtime === 'ApiEndpoint') return 'Optional; overrides the endpoint model';
  return 'e.g., gpt-5.4-mini';
}

export const REASONING_EFFORTS = ['Off', 'Minimal', 'Low', 'Medium', 'High'];
export const CAPTAIN_TIERS = ['Economy', 'Standard', 'Premium'];

/** An empty create form (auto-approve on, CLI policy inherited). */
export function emptyCaptainForm(): CaptainFormState {
  return { active: true, name: '', runtime: '', systemInstructions: '', model: '', modelEndpointId: '', reasoningEffort: '', tier: '', autoApprove: true, cliPermissionPolicy: null, ...EMPTY_MUX_CAPTAIN_FORM };
}

/** The form for editing a captain. `withPersonas` adds the persona fields (the detail page's form). */
export function captainFormFromCaptain(c: Captain, options: { withEndpoint?: boolean; withPersonas?: boolean; defaultRuntime?: string } = {}): CaptainFormState {
  const form: CaptainFormState = {
    name: c.name,
    active: c.active ?? true,
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
export type CaptainFormError = 'muxEndpointRequired' | 'inferenceEndpointRequired' | MuxFieldError;

export function captainFormError(form: CaptainFormState): CaptainFormError | null {
  if (isMuxRuntime(form.runtime) && !form.muxEndpoint.trim()) return 'muxEndpointRequired';
  const muxError = muxFormFieldError(form.runtime, form);
  if (muxError) return muxError;
  if ('modelEndpointId' in form && form.runtime === 'ApiEndpoint' && !form.modelEndpointId) return 'inferenceEndpointRequired';
  return null;
}

/** English message of a form error (pass it through the translator). */
export function captainFormErrorMessage(error: CaptainFormError): string {
  if (error === 'muxEndpointRequired') return 'Mux captains require a named Mux endpoint.';
  if (error === 'inferenceEndpointRequired') return 'API-endpoint captains require an inference endpoint. Select one, or add it under Configuration > Endpoints.';
  return muxFieldErrorMessage(error);
}

/**
 * The update request body. The server changes only the fields the body carries, so a field the form edits is always
 * sent (blank as null, which clears it) and a field the form does not have (no `modelEndpointId` or persona keys) is
 * left out and keeps its stored value. The CLI tool permission policy is never part of it (it changes through its own
 * admin endpoint on edit; `buildCaptainCreatePayload` adds it on create).
 */
export function buildCaptainPayload(form: CaptainFormState): Record<string, unknown> {
  const payload = { ...form, active: form.active ?? true } as Record<string, unknown>;
  payload.systemInstructions = form.systemInstructions ? form.systemInstructions : null;
  payload.model = form.model.trim() ? form.model.trim() : null;
  if ('modelEndpointId' in form) payload.modelEndpointId = form.runtime === 'ApiEndpoint' ? (form.modelEndpointId || null) : null;
  payload.reasoningEffort = form.reasoningEffort ? form.reasoningEffort : null;
  payload.tier = form.tier ? form.tier : null;
  if ('allowedPersonas' in form) payload.allowedPersonas = form.allowedPersonas?.trim() ? form.allowedPersonas : null;
  if ('preferredPersona' in form) payload.preferredPersona = form.preferredPersona?.trim() ? form.preferredPersona.trim() : null;
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
  // A new captain has nothing to clear: leave blank optional text out of the create body.
  if (payload.systemInstructions === null) delete payload.systemInstructions;
  if (payload.allowedPersonas === null) delete payload.allowedPersonas;
  if (payload.preferredPersona === null) delete payload.preferredPersona;
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
