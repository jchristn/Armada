import { useState } from 'react';
import { getWorkspaceDiff } from '../../api/client';
import { useLocale } from '../../context/LocaleContext';
import { parseUnifiedDiff, type UnifiedDiffLineKind } from '../../lib/unifiedDiff';
import LoadingValue from '../shared/LoadingValue';

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

const EXPANDED_KEY = 'armada.workspace.diffExpanded';

function readExpanded(): boolean {
  try {
    return localStorage.getItem(EXPANDED_KEY) === 'true';
  } catch {
    return false;
  }
}

function writeExpanded(value: boolean) {
  try {
    localStorage.setItem(EXPANDED_KEY, String(value));
  } catch {
    // Storage unavailable: the choice lasts for this page only.
  }
}

/** Files named in a unified diff's "diff --git a/x b/x" headers. */
function changedFileCount(diff: string | null): number {
  if (!diff) return 0;
  return (diff.match(/^diff --git /gm) ?? []).length;
}

/**
 * In-app review: shows a unified git diff of the vessel working tree against HEAD, with basic line coloring.
 * Collapsible to one line (its toggle, what the last load found, and Load/Refresh); loading opens it, and the open or
 * closed choice is remembered.
 */
export default function WorkspaceDiff({ vesselId }: { vesselId: string }) {
  const { t } = useLocale();
  const [diff, setDiff] = useState<string | null>(null);
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);
  const [loaded, setLoaded] = useState(false);
  const [expanded, setExpanded] = useState(() => readExpanded());

  function toggle() {
    setExpanded(!expanded);
    writeExpanded(!expanded);
  }

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
      // Show what was loaded, unless there is nothing to show.
      if (result.error || (result.diff ?? '').trim().length > 0) {
        setExpanded(true);
        writeExpanded(true);
      }
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('Failed to load diff.'));
      setExpanded(true);
    } finally {
      setLoading(false);
    }
  }

  const lines = parseUnifiedDiff(diff).lines;
  const hasDiff = !!diff && diff.trim().length > 0;
  const files = changedFileCount(diff);
  const summary = error
    ? t('Failed to load diff.')
    : !loaded
      ? t('Not loaded')
      : hasDiff
        ? t('{{count}} file(s) changed', { count: files })
        : t('No tracked changes against HEAD.');

  return (
    <div className={`card workspace-diff${expanded ? ' is-expanded' : ''}`} data-testid="workspace-diff">
      <div className="workspace-diff-header">
        <button
          type="button"
          className="workspace-diff-toggle"
          onClick={toggle}
          aria-expanded={expanded}
          title={t('Uncommitted changes to tracked files in the vessel working directory, compared with the last commit (HEAD). New untracked files are not shown. Click to show or hide.')}
        >
          <span className="workspace-readiness-chevron" aria-hidden="true">
            <svg viewBox="0 0 16 16"><path d="M6 4l4 4-4 4" /></svg>
          </span>
          <strong>{t('Review Diff')}</strong>
        </button>
        <span className={`workspace-diff-summary${error ? ' is-error' : ''}`}><LoadingValue loading={loading && !loaded && !error}>{summary}</LoadingValue></span>
        <button className="btn btn-sm" onClick={load} disabled={loading} title={loaded ? t('Reload the diff to include changes made since it was last loaded.') : t('Run git diff against HEAD in the vessel working directory and show the result here. Read-only.')}>
          {loading ? t('Loading...') : loaded ? t('Refresh') : t('Load diff')}
        </button>
      </div>
      {expanded && error && <p className="workspace-diff-error">{error}</p>}
      {expanded && hasDiff && (
        <div
          className="workspace-diff-body"
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
