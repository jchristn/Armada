import { useEffect, useRef, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { chatWithCaptain, getCaptainTools, listCaptains } from '../api/client';
import type { Captain, CaptainChatMessage, CaptainToolAccessResult, WebSocketMessage } from '../types/models';
import { useLocale } from '../context/LocaleContext';
import { useWebSocket } from '../context/WebSocketContext';
import ErrorModal from '../components/shared/ErrorModal';
import ConfirmDialog from '../components/shared/ConfirmDialog';
import CaptainChatPanel, { type ChatTurn } from '../components/shared/CaptainChatPanel';
import { applyToolEvent } from '../components/shared/ChatToolChips';
import { randomThinkingMessage } from '../components/askThinkingMessages';

// Ask Armada is available with any captain.
function isChattable(_captain: Captain): boolean {
  return true;
}

// Per-runtime setup instructions doc on GitHub for connecting a captain to Armada over MCP.
function instructionsDocUrl(runtime: string | null | undefined): string {
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

export default function AskArmada() {
  const { t } = useLocale();
  const navigate = useNavigate();
  const { subscribe } = useWebSocket();
  const [captains, setCaptains] = useState<Captain[]>([]);
  const [captainId, setCaptainId] = useState('');
  const [turns, setTurns] = useState<ChatTurn[]>([]);
  const [input, setInput] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [tools, setTools] = useState<CaptainToolAccessResult | null>(null);
  const [toolsLoading, setToolsLoading] = useState(false);
  const [streamingEnabled, setStreamingEnabled] = useState(true);
  const [showThinking, setShowThinking] = useState(false);
  const [thinking, setThinking] = useState('');
  const [confirmClearOpen, setConfirmClearOpen] = useState(false);
  const abortRef = useRef<AbortController | null>(null);
  const toolsCache = useRef<Record<string, CaptainToolAccessResult>>({});

  // Rotate the "waiting" message every 4 seconds while a turn is in flight.
  useEffect(() => {
    if (!busy) return;
    setThinking((prev) => randomThinkingMessage(prev));
    const id = window.setInterval(() => setThinking((prev) => randomThinkingMessage(prev)), 4000);
    return () => window.clearInterval(id);
  }, [busy]);

  useEffect(() => {
    listCaptains({ pageSize: 200 })
      .then((result) => {
        const chattable = result.objects.filter(isChattable);
        setCaptains(chattable);
        if (chattable.length > 0) setCaptainId((current) => current || chattable[0].id);
      })
      .catch((err: unknown) => setError(err instanceof Error ? err.message : t('Failed to load captains.')));
  }, [t]);

  // Detect whether the selected captain is connected to Armada over MCP so we can warn when it is not.
  useEffect(() => {
    if (!captainId) { setTools(null); setToolsLoading(false); return; }
    // Serve a previously-fetched result instantly; the probe is slow (it live-checks each configured
    // MCP server), so only pay for it once per captain per session.
    const cached = toolsCache.current[captainId];
    if (cached) { setTools(cached); setToolsLoading(false); return; }
    let active = true;
    setTools(null);
    setToolsLoading(true);
    getCaptainTools(captainId)
      .then((result) => { toolsCache.current[captainId] = result; if (active) setTools(result); })
      .catch(() => { if (active) setTools(null); })
      .finally(() => { if (active) setToolsLoading(false); });
    return () => { active = false; };
  }, [captainId]);

  const selectedCaptain = captains.find((c) => c.id === captainId) || null;
  // API-endpoint captains run Armada's built-in coding tools in-process and never act as an MCP
  // client, so the "connect over MCP" warning does not apply to them -- show an accurate note instead.
  const isApiEndpoint = tools != null && tools.runtime === 'ApiEndpoint';
  const armadaMcpMissing = tools != null && tools.armadaToolCount <= 0 && !isApiEndpoint;

  // Replace the in-flight streaming assistant turn (the last one) via the updater.
  function updateStreamingTurn(mutate: (turn: ChatTurn) => ChatTurn) {
    setTurns((current) => {
      const copy = [...current];
      for (let i = copy.length - 1; i >= 0; i -= 1) {
        if (copy[i].role === 'assistant' && copy[i].streaming) { copy[i] = mutate(copy[i]); break; }
      }
      return copy;
    });
  }

  async function send(message: string) {
    const text = message.trim();
    if (!text || busy || !captainId) return;
    setInput('');

    const streaming = streamingEnabled;
    // A turnId opts the request into live streaming; omit it for a plain request/response turn.
    const turnId = streaming
      ? ((typeof crypto !== 'undefined' && crypto.randomUUID) ? crypto.randomUUID() : String(Date.now()) + Math.random().toString(36).slice(2))
      : undefined;
    const history: CaptainChatMessage[] = turns
      .filter((turn) => turn.role !== 'system')
      .map((turn) => ({ role: turn.role as 'user' | 'assistant', content: turn.text }));
    setTurns((current) => streaming
      ? [...current, { role: 'user', text }, { role: 'assistant', text: '', streaming: true }]
      : [...current, { role: 'user', text }]);
    setBusy(true);

    let unsubscribe: () => void = () => undefined;
    if (streaming) {
      unsubscribe = subscribe((msg: WebSocketMessage) => {
        if (msg.type === 'ask.chunk') {
          const data = msg.data as { turnId?: string; delta?: string } | undefined;
          if (!data || data.turnId !== turnId || !data.delta) return;
          updateStreamingTurn((turn) => ({ ...turn, text: turn.text + data.delta }));
        } else if (msg.type === 'ask.tool') {
          const d = msg.data as ({ turnId?: string } & Parameters<typeof applyToolEvent>[1]) | undefined;
          if (!d || d.turnId !== turnId || !d.id) return;
          updateStreamingTurn((turn) => ({ ...turn, tools: applyToolEvent(turn.tools, d) }));
        } else if (msg.type === 'ask.thinking') {
          const data = msg.data as { turnId?: string; delta?: string } | undefined;
          if (!data || data.turnId !== turnId || !data.delta) return;
          updateStreamingTurn((turn) => ({ ...turn, thinking: (turn.thinking ?? '') + data.delta }));
        }
      });
    }

    // The controller lets the Stop button abort the request; the server then cancels the captain runtime.
    const controller = new AbortController();
    abortRef.current = controller;

    try {
      const response = await chatWithCaptain(captainId, { message: text, history, turnId, showThinking }, { signal: controller.signal });
      if (!response.success) {
        setError(response.error || t('The captain could not respond.'));
        setTurns((current) => current.filter((turn) => !(turn.role === 'assistant' && turn.streaming)).slice(0, -1));
      } else if (streaming) {
        // Reconcile the streamed text with the authoritative final reply + metrics, keeping any tool activity
        // and preferring the authoritative thinking when the server returned it.
        updateStreamingTurn((turn) => ({ ...turn, streaming: false, text: response.reply, metrics: response.metrics, model: response.model, thinking: response.thinking ?? turn.thinking }));
      } else {
        setTurns((current) => [...current, { role: 'assistant', text: response.reply, metrics: response.metrics, model: response.model, thinking: response.thinking ?? undefined }]);
      }
    } catch (err: unknown) {
      if (err instanceof Error && err.name === 'AbortError') {
        // Stop pressed: keep whatever streamed so far and just clear the in-flight flag.
        if (streaming) updateStreamingTurn((turn) => ({ ...turn, streaming: false }));
      } else {
        setError(err instanceof Error ? err.message : t('Chat failed.'));
        setTurns((current) => current.filter((turn) => !(turn.role === 'assistant' && turn.streaming)).slice(0, -1));
      }
    } finally {
      abortRef.current = null;
      unsubscribe();
      setBusy(false);
    }
  }

  // Abort the in-flight turn. The dropped request cancels the captain runtime server-side.
  function stop() {
    abortRef.current?.abort();
  }

  // Reset the conversation to an empty state. History is client-side only, so clearing the turns is
  // sufficient; also drop any transient thinking/error so the next question starts clean.
  function clearConversation() {
    setTurns([]);
    setThinking('');
    setError('');
  }

  return (
    <div className="ask-page">
      <div className="view-header">
        <div>
          <h2>{t('Ask Armada')}</h2>
          <p className="text-dim view-subtitle">{t('Chat directly with a captain.')}</p>
        </div>
        <div className="ask-header-controls">
          <label className="ask-stream-toggle" title={t('Stream the reply token-by-token as it is produced')}>
            <input type="checkbox" checked={streamingEnabled} onChange={(e) => setStreamingEnabled(e.target.checked)} disabled={busy} />
            {t('Stream responses')}
          </label>
          <label className="ask-stream-toggle" title={t('Surface the model reasoning above the answer. Mux streams it natively; other runtimes are asked to include it.')}>
            <input type="checkbox" checked={showThinking} onChange={(e) => setShowThinking(e.target.checked)} disabled={busy} />
            {t('Show thinking')}
          </label>
          <label className="text-dim" style={{ display: 'flex', flexDirection: 'column', gap: '0.25rem', fontSize: '0.8rem' }}>
            {t('Captain')}
            <select value={captainId} onChange={(e) => setCaptainId(e.target.value)} disabled={busy}>
              {captains.length === 0 && <option value="">{t('No captains available')}</option>}
              {captains.map((c) => (
                <option key={c.id} value={c.id}>{c.name} ({c.model || c.runtime})</option>
              ))}
            </select>
          </label>
        </div>
      </div>

      <ErrorModal error={error} onClose={() => setError('')} />

      {captains.length === 0 ? (
        <div className="card" style={{ padding: '1.5rem', textAlign: 'center' }}>
          <p className="text-dim">{t('No captains are configured yet. Create a captain to Ask Armada.')}</p>
        </div>
      ) : (
        <div className="ask-body">
          {toolsLoading && (
            <div className="text-dim" style={{ fontSize: '0.78rem', marginBottom: '0.6rem' }}>
              {t('Checking whether this captain is connected to Armada over MCP...')}
            </div>
          )}
          {isApiEndpoint && (
            <div className="text-dim" style={{ fontSize: '0.78rem', marginBottom: '0.6rem' }}>
              {t('This is an API-endpoint captain. It runs Armada’s built-in coding tools (read, edit, search, run) in-process against a working directory, and does not connect over MCP. Fleet, mission, and voyage orchestration tools are not available in chat.')}
            </div>
          )}
          {armadaMcpMissing && (
            <div className="mcp-warning-banner">
              <span className="mcp-warning-icon" aria-hidden="true">
                <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                  <path d="M10.29 3.86 1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z" />
                  <path d="M12 9v4" />
                  <path d="M12 17h.01" />
                </svg>
              </span>
              <div className="mcp-warning-body">
                <strong>{t('This captain is not connected to Armada over MCP.')}</strong>
                <span className="text-dim">
                  {t('It cannot call Armada tools (fleet, missions, voyages, and more). Add the Armada MCP server to this captain’s runtime config to connect it.')}
                </span>
              </div>
              <div className="mcp-warning-actions">
                <button className="btn btn-sm" onClick={() => navigate('/captains/' + captainId)}>{t('View captain')}</button>
                <a className="btn btn-sm" href={instructionsDocUrl(selectedCaptain?.runtime)} target="_blank" rel="noopener noreferrer">{t('How to connect')}</a>
              </div>
            </div>
          )}

          <CaptainChatPanel
            t={t}
            turns={turns}
            notice={selectedCaptain?.runtime === 'Codex' ? t('Codex responses cannot be streamed and will arrive upon completion.') : undefined}
            assistantName={selectedCaptain?.name}
            emptyState={<p>{selectedCaptain ? t('Chatting with {{name}}', { name: selectedCaptain.name }) : t('Select a captain to begin.')}</p>}
            input={input}
            onInputChange={setInput}
            onSend={() => send(input)}
            onStop={stop}
            busy={busy}
            canSend={!busy && !!captainId && input.trim().length > 0}
            canStop={busy}
            thinking={thinking}
            inputPlaceholder={t('Message the captain...')}
            inputDisabled={!captainId}
            onClear={() => setConfirmClearOpen(true)}
            clearDisabled={busy || turns.length === 0}
          />
        </div>
      )}

      <ConfirmDialog
        open={confirmClearOpen}
        title={t('Clear Conversation')}
        message={t('Clear this conversation? The messages shown here will be removed.')}
        confirmLabel={t('Clear')}
        cancelLabel={t('Cancel')}
        danger
        onConfirm={() => {
          setConfirmClearOpen(false);
          clearConversation();
        }}
        onCancel={() => setConfirmClearOpen(false)}
      />
    </div>
  );
}
