import ActionMenu from '../shared/ActionMenu';
import type { WorkspaceTreeEntry } from '../../types/models';

interface WorkspaceTreeProps {
  entriesByDirectory: Record<string, WorkspaceTreeEntry[]>;
  expandedPaths: Record<string, boolean>;
  activePath: string | null;
  selectedPaths: string[];
  loadingDirectories: string[];
  onToggleDirectory: (path: string) => void;
  onOpenFile: (path: string) => void;
  onToggleSelect: (path: string) => void;
  onRenameEntry: (entry: WorkspaceTreeEntry) => void;
  onDeleteEntry: (entry: WorkspaceTreeEntry) => void;
  onViewMetadata: (entry: WorkspaceTreeEntry) => void;
}

function TreeBranch(props: WorkspaceTreeProps & { directoryPath: string; depth: number }) {
  const {
    entriesByDirectory,
    expandedPaths,
    activePath,
    selectedPaths,
    loadingDirectories,
    onToggleDirectory,
    onOpenFile,
    onToggleSelect,
    onRenameEntry,
    onDeleteEntry,
    onViewMetadata,
    directoryPath,
    depth,
  } = props;

  const entries = entriesByDirectory[directoryPath] || [];

  return (
    <>
      {entries.map((entry) => {
        const entryPath = entry.relativePath;
        const expanded = !!expandedPaths[entryPath];
        const selected = selectedPaths.includes(entryPath);
        const loading = loadingDirectories.includes(entryPath);

        const entryMenuItems = [
          {
            label: entry.isDirectory ? (expanded ? 'Collapse' : 'Open') : 'Open',
            onClick: () => (entry.isDirectory ? onToggleDirectory(entryPath) : onOpenFile(entryPath)),
          },
          {
            label: 'Rename',
            onClick: () => onRenameEntry(entry),
          },
          {
            label: 'Delete',
            onClick: () => onDeleteEntry(entry),
            danger: true,
          },
          {
            label: 'View Metadata',
            onClick: () => onViewMetadata(entry),
          },
        ];

        return (
          <div key={entryPath}>
            <div
              className={`workspace-tree-row${activePath === entryPath ? ' active' : ''}${selected ? ' selected' : ''}`}
              style={{ paddingLeft: `${0.55 + depth * 0.9}rem` }}
            >
              <div className="workspace-tree-row-main">
                {entry.isDirectory ? (
                  <button
                    type="button"
                    className="workspace-tree-toggle"
                    onClick={() => {
                      if (!loading) {
                        onToggleDirectory(entryPath);
                      }
                    }}
                    disabled={loading}
                    title={expanded ? 'Collapse folder' : 'Expand folder'}
                    aria-expanded={expanded}
                  >
                    <span className={`workspace-tree-disclosure${expanded ? ' expanded' : ''}`} aria-hidden="true">
                      <svg viewBox="0 0 12 12" focusable="false" aria-hidden="true">
                        <path d="M4 2.5L8 6L4 9.5" />
                      </svg>
                    </span>
                  </button>
                ) : (
                  <span className="workspace-tree-toggle-spacer" aria-hidden="true" />
                )}

                {!entry.isDirectory ? (
                  <input aria-label="Select file for Workspace actions"
                    className="workspace-tree-checkbox"
                    type="checkbox"
                    checked={selected}
                    onChange={() => onToggleSelect(entryPath)}
                    title="Select file for Workspace actions"
                  />
                ) : (
                  <span className="workspace-tree-checkbox-spacer" aria-hidden="true" />
                )}

                <button
                  type="button"
                  className={`workspace-tree-entry${entry.isDirectory ? ' directory' : ''}`}
                  onClick={() => {
                    if (entry.isDirectory) {
                      if (!loading) {
                        onToggleDirectory(entryPath);
                      }
                      return;
                    }

                    onOpenFile(entryPath);
                  }}
                  title={entry.relativePath}
                >
                  {entry.isDirectory && <span className="workspace-tree-folder-icon" aria-hidden="true" />}
                  <span className="workspace-tree-entry-name">{entry.name}</span>
                  {!entry.isDirectory && !entry.isEditable && (
                    <span className="workspace-tree-chip">RO</span>
                  )}
                  {loading && <span className="workspace-tree-chip">...</span>}
                </button>
              </div>

              <div className="workspace-tree-row-actions">
                <ActionMenu id={`workspace-tree-${entryPath}`} items={entryMenuItems} />
              </div>
            </div>

            {entry.isDirectory && expanded && (
              <TreeBranch
                entriesByDirectory={entriesByDirectory}
                expandedPaths={expandedPaths}
                activePath={activePath}
                selectedPaths={selectedPaths}
                loadingDirectories={loadingDirectories}
                onToggleDirectory={onToggleDirectory}
                onOpenFile={onOpenFile}
                onToggleSelect={onToggleSelect}
                onRenameEntry={onRenameEntry}
                onDeleteEntry={onDeleteEntry}
                onViewMetadata={onViewMetadata}
                directoryPath={entryPath}
                depth={depth + 1}
              />
            )}
          </div>
        );
      })}
    </>
  );
}

export default function WorkspaceTree(props: WorkspaceTreeProps) {
  return (
    <div className="workspace-tree">
      <TreeBranch {...props} directoryPath="" depth={0} />
    </div>
  );
}
