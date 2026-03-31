import { useState, useCallback } from 'react';
import { copyToClipboard } from './CopyButton';

interface JsonViewerProps {
  open: boolean;
  title: string;
  subtitle?: string;
  id?: string;
  data: unknown;
  onClose: () => void;
}

export default function JsonViewer({ open, title, subtitle, id, data, onClose }: JsonViewerProps) {
  const [copied, setCopied] = useState(false);

  const content = typeof data === 'string' ? data : JSON.stringify(data, null, 2);

  const handleCopy = useCallback(() => {
    copyToClipboard(content).then(() => {
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    }).catch(() => {});
  }, [content]);

  if (!open) return null;

  return (
    <div className="json-viewer-overlay" onClick={onClose}>
      <div className="json-viewer-modal" onClick={e => e.stopPropagation()}>
        <div className="json-viewer-header">
          <h3>{title}</h3>
          <button className="json-viewer-close" onClick={onClose}>&times;</button>
        </div>
        {(subtitle || id) && (
          <div className="json-viewer-sub">
            {id && <span className="mono">{id}</span>}
            {subtitle && <span className="text-dim">{subtitle}</span>}
          </div>
        )}
        <div className="json-viewer-body">
          <button
            className={`json-viewer-copy btn btn-sm${copied ? ' copied' : ''}`}
            onClick={handleCopy}
          >
            {copied ? 'Copied!' : 'Copy'}
          </button>
          <pre>{content}</pre>
        </div>
      </div>
    </div>
  );
}
