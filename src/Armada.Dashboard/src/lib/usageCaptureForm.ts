import type { Captain, UsageCaptureMode } from '../types/models';

export interface UsageCaptureFormState {
  enabled: boolean;
  mode: UsageCaptureMode;
  interval: string;
  executable: string;
  argumentsText: string;
}

export const USAGE_INTERVAL_MIN = 60;
export const USAGE_INTERVAL_MAX = 86400;
export const USAGE_INTERVAL_DEFAULT = 300;
const SHELL_SYNTAX = /[;|&<>`'"]|\$\(/;

/** Form state from a captain; a minimally deserialized captain (no usage fields) yields off/Auto/300. */
export function usageCaptureFormFromCaptain(captain: Partial<Captain>): UsageCaptureFormState {
  let executable = '';
  let args: string[] = [];
  if (captain.usageCaptureCommand) {
    try {
      const parsed = JSON.parse(captain.usageCaptureCommand) as { executable?: unknown; arguments?: unknown };
      if (typeof parsed.executable === 'string') executable = parsed.executable;
      if (Array.isArray(parsed.arguments)) args = parsed.arguments.filter((a): a is string => typeof a === 'string');
    } catch {
      executable = '';
    }
  }
  return {
    enabled: captain.usageCaptureEnabled === true,
    mode: captain.usageCaptureMode ?? 'Auto',
    interval: String(captain.usageCaptureIntervalSeconds ?? USAGE_INTERVAL_DEFAULT),
    executable,
    argumentsText: args.join('\n'),
  };
}

export type UsageCaptureFormError = 'intervalInvalid' | 'executableRequired' | 'executableShellSyntax';

export const USAGE_CAPTURE_FORM_ERROR_TEXT: Record<UsageCaptureFormError, string> = {
  intervalInvalid: 'Interval must be a whole number of seconds from 60 to 86400.',
  executableRequired: 'Custom capture needs an executable path.',
  executableShellSyntax: 'The executable must be a path, not shell syntax. Put each argument on its own line.',
};

export function usageCaptureFormError(form: UsageCaptureFormState): UsageCaptureFormError | null {
  const interval = Number(form.interval);
  if (!/^\d+$/.test(form.interval.trim()) || interval < USAGE_INTERVAL_MIN || interval > USAGE_INTERVAL_MAX) return 'intervalInvalid';
  if (form.mode === 'Custom') {
    if (!form.executable.trim()) return 'executableRequired';
    if (SHELL_SYNTAX.test(form.executable)) return 'executableShellSyntax';
  }
  return null;
}

/** Partial update body carrying only the usage fields (the server keeps every other captain field). */
export function buildUsageCapturePayload(form: UsageCaptureFormState): Partial<Captain> {
  const args = form.argumentsText.split('\n').map((a) => a.trim()).filter((a) => a.length > 0);
  return {
    usageCaptureEnabled: form.enabled,
    usageCaptureIntervalSeconds: Number(form.interval),
    usageCaptureMode: form.mode,
    usageCaptureCommand: form.mode === 'Custom' ? JSON.stringify({ executable: form.executable.trim(), arguments: args }) : null,
  };
}
