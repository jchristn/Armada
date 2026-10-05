import { useState, useCallback, useMemo } from 'react';
import { copyToClipboard } from './CopyButton';
import { useLocale } from '../../context/LocaleContext';
import { parseUnifiedDiff, type UnifiedDiffLine } from '../../lib/unifiedDiff';

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

/** Render classified diff lines (a slice of the parser's output) as HTML. */
function renderDiffLines(lines: UnifiedDiffLine[]): string {
  let html = '';
  for (const line of lines) {
    const escaped = escapeHtml(line.text);
    switch (line.kind) {
      case 'fileHeader':
        html += `<div class="diff-file-header">${escaped}</div>`;
        break;
      case 'hunkHeader':
        html += `<div class="diff-hunk-header">${escaped}</div>`;
        break;
      case 'meta':
        html += `<div class="diff-meta-line">${escaped}</div>`;
        break;
      case 'add':
        html += `<div class="diff-line diff-line-add"><span class="diff-line-num diff-line-num-old"></span><span class="diff-line-num diff-line-num-new">${line.newNumber ?? ''}</span><span class="diff-line-content">${escaped}</span></div>`;
        break;
      case 'del':
        html += `<div class="diff-line diff-line-del"><span class="diff-line-num diff-line-num-old">${line.oldNumber ?? ''}</span><span class="diff-line-num diff-line-num-new"></span><span class="diff-line-content">${escaped}</span></div>`;
        break;
      default:
        html += `<div class="diff-line diff-line-ctx"><span class="diff-line-num diff-line-num-old">${line.oldNumber || ''}</span><span class="diff-line-num diff-line-num-new">${line.newNumber || ''}</span><span class="diff-line-content">${escaped}</span></div>`;
        break;
    }
  }
  return html;
}

export default function DiffViewer({ open, title, rawDiff, loading, onClose }: DiffViewerProps) {
  const { t } = useLocale();
  // Index of the selected file section (sections are identified by position, not by re-matching names).
  const [selectedFile, setSelectedFile] = useState<number | null>(null);
  const [copied, setCopied] = useState(false);

  const parsed = useMemo(
    () => (rawDiff && rawDiff !== 'No changes' ? parseUnifiedDiff(rawDiff) : parseUnifiedDiff(null)),
    [rawDiff],
  );
  const files = parsed.files;
  const totalAdditions = files.reduce((s, f) => s + f.additions, 0);
  const totalDeletions = files.reduce((s, f) => s + f.deletions, 0);

  const isEmpty = !rawDiff || !rawDiff.trim() || rawDiff === 'No changes' || rawDiff === 'No modified files';

  const contentHtml = useMemo(() => {
    if (isEmpty) {
      return `<div class="diff-empty-state"><span class="text-dim">${t('No modified files')}</span></div>`;
    }
    const section = selectedFile !== null ? files[selectedFile] : undefined;
    if (section) {
      return renderDiffLines(parsed.lines.slice(section.startLine, section.endLine));
    }
    return renderDiffLines(parsed.lines);
  }, [parsed, files, selectedFile, isEmpty, t]);

  const handleCopy = useCallback(() => {
    copyToClipboard(rawDiff).then(() => {
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    }).catch(() => {});
  }, [rawDiff]);

  const handleFileClick = useCallback((fileIndex: number) => {
    setSelectedFile(prev => prev === fileIndex ? null : fileIndex);
  }, []);

  if (!open) return null;

  return (
    <div className="diff-modal-overlay" onClick={onClose}>
      <div className="diff-modal" onClick={e => e.stopPropagation()}>
        <div className="diff-modal-header">
          <h3 className="viewer-title">{title}</h3>
          {!isEmpty && (
            <div className="diff-modal-stats">
              <span className="diff-stat-files">
                {files.length} {t(files.length === 1 ? 'file changed' : 'files changed')}
              </span>
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
                {copied ? t('Copied!') : t('Copy Raw')}
              </button>
            )}
            <button className="btn btn-sm" onClick={onClose}>{t('Close')}</button>
          </div>
        </div>
        <div className="diff-modal-body">
          {files.length > 0 && (
            <div className="diff-file-nav">
              <div className="diff-file-nav-header">
                {t('Files')} ({files.length})
              </div>
              {files.map((f, fileIndex) => {
                const pathParts = f.path.split('/');
                const fileName = pathParts.pop() || f.path;
                const dirPath = pathParts.join('/');
                return (
                  <div
                    key={`${fileIndex}:${f.path}`}
                    className={`diff-file-nav-item${selectedFile === fileIndex ? ' active' : ''}`}
                    onClick={() => handleFileClick(fileIndex)}
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
                  <span className="text-dim">{t('Loading diff...')}</span>
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
