import { useState, useCallback } from 'react';

/**
 * Copy text to clipboard with fallback for non-HTTPS contexts.
 * Works on https, http, localhost, and file:// origins.
 */
export function copyToClipboard(text: string): Promise<void> {
  // navigator.clipboard is only available in secure contexts (https, localhost)
  if (navigator.clipboard?.writeText) {
    return navigator.clipboard.writeText(text).catch(() => fallbackCopy(text));
  }
  return fallbackCopy(text);
}

function fallbackCopy(text: string): Promise<void> {
  return new Promise((resolve, reject) => {
    const textarea = document.createElement('textarea');
    textarea.value = text;
    // Move off-screen to avoid visual flash
    textarea.style.position = 'fixed';
    textarea.style.left = '-9999px';
    textarea.style.top = '-9999px';
    textarea.style.opacity = '0';
    document.body.appendChild(textarea);
    textarea.focus();
    textarea.select();
    try {
      const ok = document.execCommand('copy');
      document.body.removeChild(textarea);
      if (ok) resolve();
      else reject(new Error('execCommand copy failed'));
    } catch (err) {
      document.body.removeChild(textarea);
      reject(err);
    }
  });
}

interface CopyButtonProps {
  text: string;
  className?: string;
  title?: string;
  onClick?: (e: React.MouseEvent) => void;
}

export default function CopyButton({
  text,
  className = 'copy-btn',
  title = 'Copy to clipboard',
  onClick,
}: CopyButtonProps) {
  const [copied, setCopied] = useState(false);

  const handleCopy = useCallback((e: React.MouseEvent) => {
    onClick?.(e);
    e.stopPropagation();
    copyToClipboard(text).then(() => {
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    }).catch(() => {
      // silently fail
    });
  }, [onClick, text]);

  return (
    <button
      className={`${className}${copied ? ' copied' : ''}`}
      onClick={handleCopy}
      title={copied ? 'Copied!' : title}
      type="button"
    />
  );
}
