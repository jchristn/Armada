import { useState } from 'react';
import { getWorkspaceDiff } from '../../api/client';
import { useLocale } from '../../context/LocaleContext';
import { parseUnifiedDiff, type UnifiedDiffLineKind } from '../../lib/unifiedDiff';

/** Color by the parser's line classification (an added line reading "+++ x" inside a hunk is still an addition). */
function lineColor(kind: UnifiedDiffLineKind): string | undefined {
  switch (kind) {
    case 'add': return 'var(--success, #4caf50)';
    case 'del': return '#fca5a5';
    case 'hunkHeader': return '#93c5fd';
    case 'fileHeader':
    case 'meta': return '#94a3b8';
    default: return undefined;
  }
}

/**
 * In-app review: shows a unified git diff of the vessel working tree against HEAD, with basic
 * line coloring. Reviewers can eyeball the changes without leaving the dashboard.
 */
export default function WorkspaceDiff({ vesselId }: { vesselId: string }) {
  const { t } = useLocale();
  const [diff, setDiff] = useState<string | null>(null);
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);
  const [loaded, setLoaded] = useState(false);

  async function load() {
    setLoading(true);
    try {
      const result = await getWorkspaceDiff(vesselId);
      if (result.error) {
        setError(result.error);
        setDiff(null);
      } else {
        setDiff(result.diff);
        setError('');
      }
      setLoaded(true);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('Failed to load diff.'));
    } finally {
      setLoading(false);
    }
  }

  const lines = parseUnifiedDiff(diff).lines;

  return (
    <div className="card" style={{ padding: '0.75rem', marginTop: '1rem' }}>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '0.5rem' }}>
        <strong>{t('Review Diff')}</strong>
        <button className="btn btn-sm" onClick={load} disabled={loading}>
          {loading ? t('Loading...') : loaded ? t('Refresh') : t('Load diff')}
        </button>
      </div>
      {error && <p style={{ color: 'var(--danger, #ff6b6b)' }}>{error}</p>}
      {loaded && !error && (diff ?? '').trim().length === 0 && (
        <p className="text-dim">{t('No tracked changes against HEAD.')}</p>
      )}
      {diff && diff.trim().length > 0 && (
        <div
          style={{
            background: 'var(--code-bg, #0b0e14)',
            fontFamily: 'monospace',
            fontSize: '0.8rem',
            padding: '0.6rem',
            borderRadius: '4px',
            maxHeight: '480px',
            overflow: 'auto',
          }}
        >
          {lines.map((line, i) => (
            <pre key={i} style={{ margin: 0, whiteSpace: 'pre-wrap', color: lineColor(line.kind) }}>{line.text || ' '}</pre>
          ))}
        </div>
      )}
    </div>
  );
}
