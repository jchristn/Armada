import { useState, useCallback } from 'react';
import { copyToClipboard } from './CopyButton';
import { useLocale } from '../../context/LocaleContext';

interface JsonViewerProps {
  open: boolean;
  title: string;
  subtitle?: string;
  id?: string;
  data: unknown;
  onClose: () => void;
}

export default function JsonViewer({ open, title, subtitle, id, data, onClose }: JsonViewerProps) {
  const { t } = useLocale();
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
          {/* Titles arrive already rendered (translated label plus entity name); they are not passed through t() again. */}
          <h3>{title}</h3>
          <button className="json-viewer-close" onClick={onClose} title={t('Close')} aria-label={t('Close')}>&times;</button>
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
            {copied ? t('Copied!') : t('Copy')}
          </button>
          <pre data-i18n-skip="true">{content}</pre>
        </div>
      </div>
    </div>
  );
}
