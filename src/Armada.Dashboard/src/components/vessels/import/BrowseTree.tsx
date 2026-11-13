import { useCallback, useEffect, useState } from 'react';
import { apiErrorCode, browseAllVesselImport } from '../../../api/client';
import type { VesselBrowseEntry } from '../../../types/models';
import { useLocale } from '../../../context/LocaleContext';
import CodeStatusBadge from '../../shared/CodeStatusBadge';
import { importErrorLabel } from '../../../lib/vesselImportLabels';

interface BrowseTreeProps {
  selected: string[];
  onToggle: (path: string) => void;
  /** When false, worktree directories are shown but cannot be selected. */
  allowWorktrees: boolean;
}

interface NodeState {
  loading: boolean;
  error: string;
  entries: VesselBrowseEntry[] | null;
  expanded: boolean;
  truncated?: boolean;
}

/**
 * Lazily expanded folder tree over the Admiral host filesystem (limited server-side to the allowed import
 * roots). Git repositories and worktrees are marked; checking a folder adds it to the discovery input.
 */
export default function BrowseTree({ selected, onToggle, allowWorktrees }: BrowseTreeProps) {
  const { t } = useLocale();
  const [roots, setRoots] = useState<VesselBrowseEntry[] | null>(null);
  const [rootsTruncated, setRootsTruncated] = useState(false);
  const [rootError, setRootError] = useState('');
  const [rootLoading, setRootLoading] = useState(false);
  const [nodes, setNodes] = useState<Record<string, NodeState>>({});

  const describeError = useCallback((err: unknown) => {
    const fallback = err instanceof Error ? err.message : t('Failed to list the directory.');
    return importErrorLabel(t, apiErrorCode(err), fallback);
  }, [t]);

  const loadRoots = useCallback(async () => {
    setRootLoading(true);
    setRootError('');
    try {
      // Every page of the listing is read explicitly; the server pages it (1000 per page).
      const result = await browseAllVesselImport(null);
      setRoots(result.entries || []);
      setRootsTruncated(Boolean(result.truncated));
    } catch (err: unknown) {
      setRootError(describeError(err));
    } finally {
      setRootLoading(false);
    }
  }, [describeError]);

  useEffect(() => { void loadRoots(); }, [loadRoots]);

  async function toggleExpand(entry: VesselBrowseEntry) {
    const current = nodes[entry.path];
    if (current?.expanded) {
      setNodes((n) => ({ ...n, [entry.path]: { ...current, expanded: false } }));
      return;
    }
    if (current?.entries) {
      setNodes((n) => ({ ...n, [entry.path]: { ...current, expanded: true } }));
      return;
    }
    await loadNode(entry);
  }

  async function loadNode(entry: VesselBrowseEntry) {
    setNodes((n) => ({ ...n, [entry.path]: { loading: true, error: '', entries: null, expanded: true } }));
    try {
      const result = await browseAllVesselImport(entry.path);
      setNodes((n) => ({ ...n, [entry.path]: { loading: false, error: '', entries: result.entries || [], expanded: true, truncated: Boolean(result.truncated) } }));
    } catch (err: unknown) {
      setNodes((n) => ({ ...n, [entry.path]: { loading: false, error: describeError(err), entries: null, expanded: true } }));
    }
  }

  function renderEntries(entries: VesselBrowseEntry[], depth: number) {
    if (entries.length === 0) {
      return <li className="browse-tree-empty text-dim" style={{ paddingLeft: `${depth * 1.1 + 1.6}rem` }}>{t('No subdirectories')}</li>;
    }
    return entries.map((entry) => {
      const node = nodes[entry.path];
      const disabled = entry.isWorktree && !allowWorktrees;
      const checked = selected.includes(entry.path);
      return (
        <li key={entry.path} className="browse-tree-item">
          <div className="browse-tree-row" style={{ paddingLeft: `${depth * 1.1}rem` }}>
            {entry.hasSubdirectories && !entry.isGitRepository ? (
              <button
                type="button"
                className="browse-tree-expand"
                aria-expanded={Boolean(node?.expanded)}
                aria-label={node?.expanded ? t('Collapse {{name}}', { name: entry.name }) : t('Expand {{name}}', { name: entry.name })}
                title={node?.expanded ? t('Collapse {{name}}', { name: entry.name }) : t('Expand {{name}}', { name: entry.name })}
                onClick={() => void toggleExpand(entry)}
              >
                <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" aria-hidden="true" style={{ transform: node?.expanded ? 'rotate(90deg)' : undefined }}><path d="m9 18 6-6-6-6" /></svg>
              </button>
            ) : (
              <span className="browse-tree-expand-spacer" aria-hidden="true" />
            )}
            <label className={`browse-tree-label${disabled ? ' disabled' : ''}`} title={t('{{path}}: check to search this folder for git repositories.', { path: entry.path })}>
              <input title={t('{{path}}: check to search this folder for git repositories.', { path: entry.path })} type="checkbox" checked={checked} disabled={disabled && !checked} onChange={() => onToggle(entry.path)} />
              <span className="browse-tree-name" data-i18n-skip="true">{entry.name}</span>
            </label>
            {entry.isGitRepository && <CodeStatusBadge label={t('Git repository')} tone="success" icon="check" title={t('This folder is a git repository and can be imported as a vessel.')} />}
            {entry.isWorktree && <CodeStatusBadge label={t('Worktree')} tone="warning" icon="alert" title={disabled ? t('This folder is a git worktree or submodule of another repository. Worktrees are excluded unless you allow them below.') : t('This folder is a git worktree or submodule of another repository.')} />}
          </div>
          {node?.expanded && (
            <ul className="browse-tree-children">
              {node.loading && <li className="text-dim browse-tree-empty" style={{ paddingLeft: `${(depth + 1) * 1.1 + 1.6}rem` }}>{t('Loading...')}</li>}
              {node.error && (
                <li className="field-error browse-tree-empty" style={{ paddingLeft: `${(depth + 1) * 1.1 + 1.6}rem` }}>
                  {node.error}{' '}
                  <button type="button" className="btn btn-sm" onClick={() => void loadNode(entry)} title={t('Try listing this folder again.')}>{t('Retry')}</button>
                </li>
              )}
              {node.entries && renderEntries(node.entries, depth + 1)}
              {node.truncated && <li className="text-dim browse-tree-empty browse-tree-truncated" style={{ paddingLeft: `${(depth + 1) * 1.1 + 1.6}rem` }}>{t('This folder has more subdirectories than can be listed. Open a narrower folder to see the rest.')}</li>}
            </ul>
          )}
        </li>
      );
    });
  }

  return (
    <div className="browse-tree" role="group" aria-label={t('Folders on the Admiral host')}>
      {rootLoading && <p className="text-dim">{t('Loading allowed roots...')}</p>}
      {rootError && (
        <div className="alert alert-error" role="alert">
          {rootError} <button type="button" className="btn btn-sm" onClick={() => void loadRoots()} title={t('Try loading the allowed import folders again.')}>{t('Retry')}</button>
        </div>
      )}
      {roots && roots.length === 0 && <p className="text-dim">{t('No browsable folders. An administrator can configure allowed roots under Settings > Import.')}</p>}
      {roots && roots.length > 0 && <ul className="browse-tree-root">{renderEntries(roots, 0)}</ul>}
      {rootsTruncated && <p className="text-dim browse-tree-truncated">{t('This folder has more subdirectories than can be listed. Open a narrower folder to see the rest.')}</p>}
    </div>
  );
}
