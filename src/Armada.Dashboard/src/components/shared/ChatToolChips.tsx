// Shared tool-call activity chips used by both Ask Armada and the Planning "Current Session" chat.
// Renders each tool call as a compact, collapsible chip: status glyph, name, runtime, and an
// expandable body showing the call arguments and result.

import { Fragment } from 'react';
import type { ToolEvent } from '../../lib/toolEvents';
import { formatToolMs, prettyJson, toolResultPreview as resultPreview } from '../../lib/askFormat';

export { applyToolEvent, type ToolEvent, type ToolEventMessage } from '../../lib/toolEvents';

interface ChatToolChipsProps {
  tools: ToolEvent[] | undefined;
  /** Runtime that executed these tools (e.g. "ApiEndpoint", "Mux"); shown as a badge on each card. */
  runtimeLabel?: string;
  runningLabel: string;
  argumentsLabel: string;
  resultLabel: string;
  noDetailsLabel: string;
  /** Explanation shown under a call the CLI refused for lack of permission (already localized). */
  permissionDeniedNote?: string;
  /** Accessible label of the refused marker (already localized). */
  permissionDeniedLabel?: string;
}

export default function ChatToolChips({ tools, runtimeLabel, runningLabel, argumentsLabel, resultLabel, noDetailsLabel, permissionDeniedNote, permissionDeniedLabel }: ChatToolChipsProps) {
  if (!tools || tools.length === 0) return null;
  return (
    <div className="chat-tools">
      {tools.map((tool) => (
        <Fragment key={tool.id}>
        <details className={`chat-tool chat-tool-${tool.status}${tool.permissionDenied ? ' chat-tool-denied' : ''}`}>
          <summary className="chat-tool-summary">
            {tool.permissionDenied && <span className="chat-tool-status" aria-hidden="true">{'\u2715!'}</span>}
            {!tool.permissionDenied && (
            <span className="chat-tool-status" aria-hidden="true">
              {tool.status === 'running' ? '…' : tool.status === 'success' ? '✓' : '✕'}
            </span>
            )}
            {tool.permissionDenied && permissionDeniedLabel && <span className="sr-only">{permissionDeniedLabel}</span>}
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
        {tool.permissionDenied && permissionDeniedNote && (
          <div className="chat-tool-denied-note" role="note">{permissionDeniedNote}</div>
        )}
        </Fragment>
      ))}
    </div>
  );
}
