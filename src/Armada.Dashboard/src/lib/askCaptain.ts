import type { CaptainToolAccessResult } from '../types/models';

// How the open Ask conversation's captain reaches Armada: whether it can propose actions (approval cards), runs
// Armada tools ungated, or is not connected at all. Shared by the dashboard and the mobile app.

/** Per-runtime setup instructions on GitHub for connecting a captain to Armada over MCP. */
export function instructionsDocUrl(runtime: string | null | undefined): string {
  const files: Record<string, string> = {
    ClaudeCode: 'INSTRUCTIONS_FOR_CLAUDE_CODE.md',
    Codex: 'INSTRUCTIONS_FOR_CODEX.md',
    Cursor: 'INSTRUCTIONS_FOR_CURSOR.md',
    Gemini: 'INSTRUCTIONS_FOR_GEMINI.md',
    Mux: 'INSTRUCTIONS_FOR_MUX.md',
    OpenCode: 'INSTRUCTIONS_FOR_OPENCODE.md',
  };
  const file = (runtime && files[runtime]) || 'MCP_API.md';
  return 'https://github.com/jchristn/Armada/blob/main/docs/' + file;
}

export interface AskCaptainAccess {
  /** No captain is chosen: plain messages cannot be sent, quick actions still work. */
  noCaptain: boolean;
  /** The captain is not connected to Armada over MCP: it can answer but cannot propose actions. */
  mcpMissing: boolean;
  /** The captain uses its own Armada connection: its Armada tool calls run without approval cards. */
  ungated: boolean;
}

/**
 * The server connects every supported runtime's captain to Armada's MCP tools for each thread turn with a
 * thread-scoped token (askApprovalGated), so its own host MCP configuration does not matter there and its mutating
 * Armada tool calls become approval cards. Older servers do not send the flag; fall back to the two runtimes they
 * gated. `tools` is null until the captain's tool access has loaded.
 */
export function askCaptainAccess(captainId: string | null | undefined, tools: CaptainToolAccessResult | null | undefined): AskCaptainAccess {
  const serverProvidesMcp = tools?.askApprovalGated ?? (tools?.runtime === 'ApiEndpoint' || tools?.runtime === 'ClaudeCode');
  const mcpMissing = !!captainId && tools != null && tools.armadaToolCount <= 0 && !serverProvidesMcp;
  const ungated = !!captainId && tools != null && !serverProvidesMcp && !mcpMissing;
  return { noCaptain: !captainId, mcpMissing, ungated };
}
