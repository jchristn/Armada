import { useRef, useState } from 'react';
import { execWorkspaceCommand } from '../../api/client';
import { useLocale } from '../../context/LocaleContext';

interface TerminalLine {
  kind: 'command' | 'stdout' | 'stderr' | 'meta';
  text: string;
}

/**
 * In-browser dock terminal: runs a shell command in the vessel's working tree and shows the
 * captured output. Non-interactive (one command per run), bounded by a server-side timeout, and
 * restricted to tenant administrators by the backend.
 */
export default function WorkspaceTerminal({ vesselId }: { vesselId: string }) {
  const { t } = useLocale();
  const [command, setCommand] = useState('');
  const [lines, setLines] = useState<TerminalLine[]>([]);
  const [busy, setBusy] = useState(false);
  const [history, setHistory] = useState<string[]>([]);
  const [historyIndex, setHistoryIndex] = useState(-1);
  const endRef = useRef<HTMLDivElement | null>(null);

  async function run() {
    const cmd = command.trim();
    if (!cmd || busy) return;
    setCommand('');
    setHistory((h) => [cmd, ...h].slice(0, 50));
    setHistoryIndex(-1);
    setLines((l) => [...l, { kind: 'command', text: '$ ' + cmd }]);
    setBusy(true);
    try {
      const result = await execWorkspaceCommand(vesselId, cmd);
      const next: TerminalLine[] = [];
      if (result.stdout) next.push({ kind: 'stdout', text: result.stdout.replace(/\n+$/, '') });
      if (result.stderr) next.push({ kind: 'stderr', text: result.stderr.replace(/\n+$/, '') });
      next.push({
        kind: 'meta',
        text: (result.timedOut ? t('timed out') : t('exit {{code}}', { code: result.exitCode })) + ` · ${Math.round(result.durationMs)}ms`,
      });
      setLines((l) => [...l, ...next]);
    } catch (err: unknown) {
      setLines((l) => [...l, { kind: 'stderr', text: err instanceof Error ? err.message : String(err) }]);
    } finally {
      setBusy(false);
      requestAnimationFrame(() => endRef.current?.scrollIntoView({ behavior: 'smooth' }));
    }
  }

  function onKeyDown(e: React.KeyboardEvent<HTMLInputElement>) {
    if (e.key === 'ArrowUp' && history.length > 0) {
      e.preventDefault();
      const idx = Math.min(historyIndex + 1, history.length - 1);
      setHistoryIndex(idx);
      setCommand(history[idx]);
    } else if (e.key === 'ArrowDown') {
      e.preventDefault();
      const idx = Math.max(historyIndex - 1, -1);
      setHistoryIndex(idx);
      setCommand(idx === -1 ? '' : history[idx]);
    }
  }

  const colorFor = (kind: TerminalLine['kind']) =>
    kind === 'command' ? '#93c5fd'
      : kind === 'stderr' ? '#fca5a5'
        : kind === 'meta' ? '#94a3b8'
          : undefined;

  return (
    <div className="card" style={{ padding: '0.75rem', marginTop: '1rem' }}>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '0.5rem' }}>
        <strong>{t('Terminal')}</strong>
        {lines.length > 0 && <button className="btn btn-sm" onClick={() => setLines([])}>{t('Clear')}</button>}
      </div>
      <div
        style={{
          background: 'var(--code-bg, #0b0e14)',
          color: 'var(--code-fg, #cbd5e1)',
          fontFamily: 'monospace',
          fontSize: '0.82rem',
          padding: '0.6rem',
          borderRadius: '4px',
          minHeight: '160px',
          maxHeight: '360px',
          overflow: 'auto',
        }}
      >
        {lines.length === 0 ? (
          <div style={{ color: '#94a3b8' }}>{t('Run a command in the vessel working tree (e.g. git status, ls, npm test).')}</div>
        ) : (
          lines.map((line, i) => (
            <pre key={i} style={{ margin: 0, whiteSpace: 'pre-wrap', color: colorFor(line.kind) }}>{line.text}</pre>
          ))
        )}
        <div ref={endRef} />
      </div>
      <form style={{ display: 'flex', gap: '0.5rem', marginTop: '0.5rem' }} onSubmit={(e) => { e.preventDefault(); run(); }}>
        <span style={{ fontFamily: 'monospace', alignSelf: 'center' }}>$</span>
        <input
          type="text"
          value={command}
          onChange={(e) => setCommand(e.target.value)}
          onKeyDown={onKeyDown}
          placeholder={t('Enter a command...')}
          style={{ flex: 1, fontFamily: 'monospace' }}
          disabled={busy}
          spellCheck={false}
        />
        <button type="submit" className="btn btn-primary" disabled={busy || command.trim().length === 0}>
          {busy ? t('Running...') : t('Run')}
        </button>
      </form>
    </div>
  );
}
