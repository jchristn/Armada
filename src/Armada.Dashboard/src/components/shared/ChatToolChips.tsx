// Shared tool-call activity chips used by both Ask Armada and the Planning "Current Session" chat.
// Renders each tool call as a compact, collapsible chip: status glyph, name, runtime, and an
// expandable body showing the call arguments and result.

export interface ToolEvent {
  id: string;
  name: string;
  status: 'running' | 'success' | 'failed';
  arguments?: string | null;
  result?: string | null;
  elapsedMs?: number | null;
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
    if (idx >= 0) tools[idx] = done; else tools.push(done);
  }
  return tools;
}

// Pretty-print a JSON string for the expandable tool detail; fall back to the raw text.
function prettyJson(raw: string): string {
  try { return JSON.stringify(JSON.parse(raw), null, 2); } catch { return raw; }
}

// A compact one-line preview of a tool result for the collapsed card summary: JSON is flattened to a
// single line, whitespace collapsed, and the whole thing truncated so it never wraps the summary row.
function resultPreview(raw: string): string {
  let text = raw;
  try { text = JSON.stringify(JSON.parse(raw)); } catch { /* not JSON: use as-is */ }
  text = text.replace(/\s+/g, ' ').trim();
  const max = 80;
  return text.length > max ? text.slice(0, max) + '…' : text;
}

// Compact runtime label for a tool call.
function formatToolMs(ms: number | null | undefined): string {
  if (ms == null) return '';
  if (ms < 1000) return `${Math.round(ms)}ms`;
  return `${(ms / 1000).toFixed(ms < 10000 ? 2 : 1)}s`;
}

interface ChatToolChipsProps {
  tools: ToolEvent[] | undefined;
  /** Runtime that executed these tools (e.g. "ApiEndpoint", "Mux"); shown as a badge on each card. */
  runtimeLabel?: string;
  runningLabel: string;
  argumentsLabel: string;
  resultLabel: string;
  noDetailsLabel: string;
}

export default function ChatToolChips({ tools, runtimeLabel, runningLabel, argumentsLabel, resultLabel, noDetailsLabel }: ChatToolChipsProps) {
  if (!tools || tools.length === 0) return null;
  return (
    <div className="chat-tools">
      {tools.map((tool) => (
        <details key={tool.id} className={`chat-tool chat-tool-${tool.status}`}>
          <summary className="chat-tool-summary">
            <span className="chat-tool-status" aria-hidden="true">
              {tool.status === 'running' ? '…' : tool.status === 'success' ? '✓' : '✕'}
            </span>
            <span className="chat-tool-name">{tool.name}</span>
            {runtimeLabel && <span className="chat-tool-runtime">{runtimeLabel}</span>}
            {tool.status !== 'running' && tool.result && (
              <span className="chat-tool-result-preview" title={resultPreview(tool.result)}>{resultPreview(tool.result)}</span>
            )}
            <span className="chat-tool-meta">
              {tool.status === 'running' ? runningLabel : formatToolMs(tool.elapsedMs)}
            </span>
          </summary>
          <div className="chat-tool-detail">
            {tool.arguments && (
              <>
                <div className="chat-tool-label">{argumentsLabel}</div>
                <pre className="chat-tool-pre">{prettyJson(tool.arguments)}</pre>
              </>
            )}
            {tool.result && (
              <>
                <div className="chat-tool-label">{resultLabel}</div>
                <pre className="chat-tool-pre">{prettyJson(tool.result)}</pre>
              </>
            )}
            {!tool.arguments && !tool.result && (
              <div className="text-dim" style={{ fontSize: '0.7rem' }}>{noDetailsLabel}</div>
            )}
          </div>
        </details>
      ))}
    </div>
  );
}
