import type { Captain } from '../types/models';

/**
 * Per-captain auto-approve switch stored in `runtimeOptionsJson` as `autoApprove` (absent means true). When false the
 * Admiral launches the CLI captain without its auto-approve / permission-bypass flag where the runtime supports it.
 */
export function autoApproveFromCaptain(captain: Pick<Captain, 'runtimeOptionsJson'> | null | undefined): boolean {
  const json = captain?.runtimeOptionsJson;
  if (!json) return true;
  try {
    const parsed = JSON.parse(json) as { autoApprove?: unknown };
    return parsed.autoApprove !== false;
  } catch {
    return true;
  }
}

/** Merge the auto-approve switch into a runtime options payload, keeping every other key. */
export function applyAutoApprove(runtimeOptionsJson: string | null, autoApprove: boolean): string | null {
  let payload: Record<string, unknown> = {};
  if (runtimeOptionsJson) {
    try {
      payload = JSON.parse(runtimeOptionsJson) as Record<string, unknown>;
    } catch {
      payload = {};
    }
  }
  delete payload.autoApprove;
  if (!autoApprove) payload.autoApprove = false;
  return Object.keys(payload).length > 0 ? JSON.stringify(payload) : null;
}

/** Runtimes launched as a CLI process with an auto-approve flag (ApiEndpoint captains run tools in-process). */
export function supportsAutoApproveSwitch(runtime: string | null | undefined): boolean {
  return ['ClaudeCode', 'Codex', 'Gemini', 'Cursor', 'Mux', 'OpenCode'].includes((runtime ?? '').trim());
}
