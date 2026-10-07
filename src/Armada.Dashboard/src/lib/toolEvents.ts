// Tool-call activity events (ask.tool / planning-session.tool), folded into a per-message tool list. Shared with
// the mobile app; the dashboard renders them with ChatToolChips.

export interface ToolEvent {
  id: string;
  name: string;
  status: 'running' | 'success' | 'failed';
  arguments?: string | null;
  result?: string | null;
  elapsedMs?: number | null;
  /** True when the CLI refused the call because its permission policy did not grant it. */
  permissionDenied?: boolean;
}

// A raw tool event delivered over the WebSocket (ask.tool / planning-session.tool).
export interface ToolEventMessage {
  phase?: string;
  id?: string;
  name?: string;
  arguments?: string | null;
  ok?: boolean | null;
  elapsedMs?: number | null;
  result?: string | null;
  permissionDenied?: boolean | null;
}

// Fold a WebSocket tool event into an existing tools list (matched by call id). Returns a new array.
export function applyToolEvent(existing: ToolEvent[] | undefined, d: ToolEventMessage): ToolEvent[] {
  const tools = existing ? [...existing] : [];
  if (!d.id) return tools;
  const idx = tools.findIndex((tl) => tl.id === d.id);
  if (d.phase === 'started') {
    if (idx === -1) tools.push({ id: d.id, name: d.name || 'tool', status: 'running', arguments: d.arguments ?? null });
  } else if (d.phase === 'completed') {
    const prior = idx >= 0 ? tools[idx] : undefined;
    const done: ToolEvent = {
      id: d.id,
      name: d.name || prior?.name || 'tool',
      status: d.ok === false ? 'failed' : 'success',
      arguments: prior?.arguments ?? null,
      result: d.result ?? null,
      elapsedMs: d.elapsedMs ?? null,
    };
    if (d.permissionDenied === true) {
      done.permissionDenied = true;
      done.status = 'failed';
    }
    if (idx >= 0) tools[idx] = done; else tools.push(done);
  }
  return tools;
}
