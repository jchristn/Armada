import type { Captain } from '../types/models';

export const RUNTIME_DEFAULT_MODEL_TEXT = '(runtime default)';

export interface CaptainEditFormState {
  name: string;
  runtime: string;
  model: string;
  systemInstructions: string;
  allowedPersonas: string;
  preferredPersona: string;
}

export function createCaptainEditForm(
  captain: Pick<Captain, 'name' | 'runtime' | 'model' | 'systemInstructions' | 'allowedPersonas' | 'preferredPersona'>
): CaptainEditFormState {
  return {
    name: captain.name,
    runtime: captain.runtime || 'ClaudeCode',
    model: captain.model ?? '',
    systemInstructions: captain.systemInstructions ?? '',
    allowedPersonas: captain.allowedPersonas ?? '',
    preferredPersona: captain.preferredPersona ?? '',
  };
}

export function buildCaptainUpdatePayload(form: CaptainEditFormState): Record<string, unknown> {
  const payload = { ...form, model: form.model.trim() || null } as Record<string, unknown>;
  if (!payload.systemInstructions) delete payload.systemInstructions;
  if (!payload.allowedPersonas) delete payload.allowedPersonas;
  if (!payload.preferredPersona) delete payload.preferredPersona;
  return payload;
}

export function getErrorMessage(error: unknown): string | null {
  if (error instanceof Error && error.message) return error.message;
  if (typeof error === 'string' && error) return error;
  if (!error || typeof error !== 'object') return null;

  const errorObject = error as {
    message?: unknown;
    data?: { message?: unknown; error?: unknown };
    response?: { data?: { message?: unknown; error?: unknown } };
  };

  const errorMessage = errorObject.message;
  const dataMessage = errorObject.data?.message;
  const dataError = errorObject.data?.error;
  const responseMessage = errorObject.response?.data?.message;
  const responseError = errorObject.response?.data?.error;

  if (typeof errorMessage === 'string' && errorMessage) return errorMessage;
  if (typeof dataMessage === 'string' && dataMessage) return dataMessage;
  if (typeof dataError === 'string' && dataError) return dataError;
  if (typeof responseMessage === 'string' && responseMessage) return responseMessage;
  if (typeof responseError === 'string' && responseError) return responseError;
  return null;
}

export function getCaptainSaveErrorMessage(error: unknown): string {
  return getErrorMessage(error) || 'Save failed.';
}

export function getCaptainModelDisplayText(model: string | null | undefined): string {
  return model || RUNTIME_DEFAULT_MODEL_TEXT;
}
