import { useState, useCallback, useMemo } from 'react';
import { copyToClipboard } from './CopyButton';

interface DiffFile {
  name: string;
  additions: number;
  deletions: number;
  startLine: number;
}

interface DiffViewerProps {
  open: boolean;
  title: string;
  rawDiff: string;
  loading?: boolean;
  onClose: () => void;
}

function escapeHtml(text: string): string {
  return text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
}

function parseDiffFiles(rawDiff: string): DiffFile[] {
  if (!rawDiff || rawDiff === 'No changes') return [];
  const files: DiffFile[] = [];
  const lines = rawDiff.split('\n');
  let currentFile: DiffFile | null = null;
  for (let i = 0; i < lines.length; i++) {
    const line = lines[i];
    if (line.startsWith('diff --git ')) {
      if (currentFile) files.push(currentFile);
      const match = line.match(/diff --git a\/(.*?) b\/(.*)/);
      const name = match ? match[2] : line.substring(11);
      currentFile = { name, additions: 0, deletions: 0, startLine: i };
    } else if (currentFile) {
      if (line.startsWith('+') && !line.startsWith('+++')) currentFile.additions++;
      else if (line.startsWith('-') && !line.startsWith('---')) currentFile.deletions++;
    }
  }
  if (currentFile) files.push(currentFile);
  return files;
}

function renderDiffLines(lines: string[]): string {
  let html = '';
  let oldNum = 0;
  let newNum = 0;
  for (const line of lines) {
    const escaped = escapeHtml(line);
    if (line.startsWith('diff --git ')) {
      html += `<div class="diff-file-header">${escaped}</div>`;
    } else if (line.startsWith('@@')) {
      const hunkMatch = line.match(/@@ -(\d+)/);
      if (hunkMatch) oldNum = parseInt(hunkMatch[1]);
      const newMatch = line.match(/@@ -\d+(?:,\d+)? \+(\d+)/);
      if (newMatch) newNum = parseInt(newMatch[1]);
      html += `<div class="diff-hunk-header">${escaped}</div>`;
    } else if (
      line.startsWith('---') || line.startsWith('+++') || line.startsWith('index ') ||
      line.startsWith('new file') || line.startsWith('deleted file') ||
      line.startsWith('old mode') || line.startsWith('new mode') ||
      line.startsWith('similarity index') || line.startsWith('rename from') ||
      line.startsWith('rename to') || line.startsWith('Binary files')
    ) {
      html += `<div class="diff-meta-line">${escaped}</div>`;
    } else if (line.startsWith('+')) {
      html += `<div class="diff-line diff-line-add"><span class="diff-line-num diff-line-num-old"></span><span class="diff-line-num diff-line-num-new">${newNum}</span><span class="diff-line-content">${escaped}</span></div>`;
      newNum++;
    } else if (line.startsWith('-')) {
      html += `<div class="diff-line diff-line-del"><span class="diff-line-num diff-line-num-old">${oldNum}</span><span class="diff-line-num diff-line-num-new"></span><span class="diff-line-content">${escaped}</span></div>`;
      oldNum++;
    } else {
      html += `<div class="diff-line diff-line-ctx"><span class="diff-line-num diff-line-num-old">${oldNum || ''}</span><span class="diff-line-num diff-line-num-new">${newNum || ''}</span><span class="diff-line-content">${escaped}</span></div>`;
      if (oldNum) oldNum++;
      if (newNum) newNum++;
    }
  }
  return html;
}

function renderFileDiff(rawDiff: string, fileName: string): string {
  const lines = rawDiff.split('\n');
  let inFile = false;
  const fileLines: string[] = [];
  for (const line of lines) {
    if (line.startsWith('diff --git ')) {
      if (inFile) break;
      const match = line.match(/diff --git a\/(.*?) b\/(.*)/);
      const name = match ? match[2] : '';
      if (name === fileName) inFile = true;
    }
    if (inFile) fileLines.push(line);
  }
  return renderDiffLines(fileLines);
}

export default function DiffViewer({ open, title, rawDiff, loading, onClose }: DiffViewerProps) {
  const [selectedFile, setSelectedFile] = useState<string | null>(null);
  const [copied, setCopied] = useState(false);

  const files = useMemo(() => parseDiffFiles(rawDiff), [rawDiff]);
  const totalAdditions = files.reduce((s, f) => s + f.additions, 0);
  const totalDeletions = files.reduce((s, f) => s + f.deletions, 0);

  const isEmpty = !rawDiff || !rawDiff.trim() || rawDiff === 'No changes' || rawDiff === 'No modified files';

  const contentHtml = useMemo(() => {
    if (isEmpty) {
      return '<div class="diff-empty-state"><span class="text-dim">No modified files</span></div>';
    }
    if (selectedFile) {
      return renderFileDiff(rawDiff, selectedFile);
    }
    return renderDiffLines(rawDiff.split('\n'));
  }, [rawDiff, selectedFile, isEmpty]);

  const handleCopy = useCallback(() => {
    copyToClipboard(rawDiff).then(() => {
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    }).catch(() => {});
  }, [rawDiff]);

  const handleFileClick = useCallback((fileName: string) => {
    setSelectedFile(prev => prev === fileName ? null : fileName);
  }, []);

  if (!open) return null;

  return (
    <div className="diff-modal-overlay" onClick={onClose}>
      <div className="diff-modal" onClick={e => e.stopPropagation()}>
        <div className="diff-modal-header">
          <h3 className="viewer-title">{title}</h3>
          {!isEmpty && (
            <div className="diff-modal-stats">
              <span className="diff-stat-files">{files.length} file{files.length !== 1 ? 's' : ''}</span>
              <span className="diff-stat-add">+{totalAdditions}</span>
              <span className="diff-stat-del">-{totalDeletions}</span>
            </div>
          )}
          <div className="viewer-actions">
            {!isEmpty && (
              <button
                className={`btn btn-sm${copied ? ' copied' : ''}`}
                onClick={handleCopy}
              >
                {copied ? 'Copied!' : 'Copy Raw'}
              </button>
            )}
            <button className="btn btn-sm" onClick={onClose}>Close</button>
          </div>
        </div>
        <div className="diff-modal-body">
          {files.length > 0 && (
            <div className="diff-file-nav">
              <div className="diff-file-nav-header">
                Files ({files.length})
              </div>
              {files.map(f => {
                const pathParts = f.name.split('/');
                const fileName = pathParts.pop() || f.name;
                const dirPath = pathParts.join('/');
                return (
                  <div
                    key={f.name}
                    className={`diff-file-nav-item${selectedFile === f.name ? ' active' : ''}`}
                    onClick={() => handleFileClick(f.name)}
                  >
                    <span className="diff-file-nav-name">{fileName}</span>
                    {dirPath && <span className="diff-file-nav-path">{dirPath}/</span>}
                    <div className="diff-file-nav-counts">
                      <span className="diff-file-nav-add">+{f.additions}</span>
                      <span className="diff-file-nav-del">-{f.deletions}</span>
                    </div>
                  </div>
                );
              })}
            </div>
          )}
          <div className="diff-content-wrap">
            {loading ? (
              <div className="diff-content-area">
                <div className="diff-empty-state">
                  <span className="text-dim">Loading diff...</span>
                </div>
              </div>
            ) : (
              <div
                className="diff-content-area"
                dangerouslySetInnerHTML={{ __html: contentHtml }}
              />
            )}
          </div>
        </div>
      </div>
    </div>
  );
}
