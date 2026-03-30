import { useState, useEffect, useRef, useCallback } from 'react';
import { copyToClipboard } from './CopyButton';

interface LogViewerProps {
  open: boolean;
  title: string;
  content: string;
  totalLines?: number;
  loading?: boolean;
  /** Whether the mission/process has completed (terminal state) */
  completed?: boolean;
  onClose: () => void;
  /** Called when line count changes */
  onLineCountChange?: (lines: number) => void;
  /** Called periodically when following is active - should refresh content */
  onRefresh?: () => void;
  /** Default line count */
  defaultLineCount?: number;
}

const LINE_COUNT_OPTIONS = [100, 200, 500, 1000];

export default function LogViewer({
  open,
  title,
  content,
  totalLines,
  loading,
  completed,
  onClose,
  onLineCountChange,
  onRefresh,
  defaultLineCount = 200,
}: LogViewerProps) {
  const [following, setFollowing] = useState(false);
  const [lineCount, setLineCount] = useState(defaultLineCount);
  const [copied, setCopied] = useState(false);
  const bodyRef = useRef<HTMLDivElement>(null);

  // Store onRefresh in a ref so the interval never needs to be recreated
  const onRefreshRef = useRef(onRefresh);
  onRefreshRef.current = onRefresh;

  // Use a ref for the interval ID to manage it imperatively
  const intervalRef = useRef<ReturnType<typeof setInterval> | null>(null);

  // Use a ref to track following state for the interval callback
  const followingRef = useRef(false);

  const startFollowing = useCallback(() => {
    if (intervalRef.current) return; // already running
    followingRef.current = true;
    setFollowing(true);
    onRefreshRef.current?.();
    intervalRef.current = setInterval(() => {
      if (followingRef.current) {
        onRefreshRef.current?.();
      }
    }, 1000);
  }, []);

  const stopFollowing = useCallback(() => {
    followingRef.current = false;
    setFollowing(false);
    if (intervalRef.current) {
      clearInterval(intervalRef.current);
      intervalRef.current = null;
    }
  }, []);

  const toggleFollow = useCallback(() => {
    if (followingRef.current) {
      stopFollowing();
    } else {
      startFollowing();
    }
  }, [startFollowing, stopFollowing]);

  // Auto-scroll when following and content changes
  useEffect(() => {
    if (followingRef.current && bodyRef.current) {
      bodyRef.current.scrollTop = bodyRef.current.scrollHeight;
    }
  }, [content]);

  // Stop when completed
  const prevCompleted = useRef(completed);
  useEffect(() => {
    if (completed && !prevCompleted.current && followingRef.current) {
      // One final refresh then stop
      onRefreshRef.current?.();
      stopFollowing();
    }
    prevCompleted.current = completed;
  }, [completed, stopFollowing]);

  // Cleanup on unmount
  useEffect(() => {
    return () => {
      if (intervalRef.current) {
        clearInterval(intervalRef.current);
        intervalRef.current = null;
      }
      followingRef.current = false;
    };
  }, []);

  // Reset when modal closes
  useEffect(() => {
    if (!open && followingRef.current) {
      stopFollowing();
    }
  }, [open, stopFollowing]);

  const handleLineCountChange = useCallback((count: number) => {
    setLineCount(count);
    onLineCountChange?.(count);
  }, [onLineCountChange]);

  const handleCopy = useCallback(() => {
    copyToClipboard(content).then(() => {
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    }).catch(() => {});
  }, [content]);

  if (!open) return null;

  return (
    <div className="viewer-overlay" onClick={onClose}>
      <div className="viewer-modal" onClick={e => e.stopPropagation()}>
        <div className="viewer-header">
          <h3 className="viewer-title">
            {title}
            {completed && <span className="log-live-badge" style={{ background: 'var(--tag-complete-bg, #2d4a2d)', color: 'var(--tag-complete-fg, #6fcf6f)' }}>DONE</span>}
            {following && !completed && <span className="log-live-badge">LIVE</span>}
          </h3>
          <div className="viewer-actions">
            <select
              className="log-line-select"
              value={lineCount}
              onChange={e => handleLineCountChange(parseInt(e.target.value, 10))}
            >
              {LINE_COUNT_OPTIONS.map(n => (
                <option key={n} value={n}>{n} lines</option>
              ))}
            </select>
            {onRefresh && !completed && (
              <button
                className={`btn btn-sm ${following ? 'btn-follow-active' : 'btn-follow-inactive'}`}
                onClick={toggleFollow}
                title={following ? 'Stop following' : 'Follow (auto-refresh)'}
              >
                <span className={`follow-dot${following ? ' follow-dot-active' : ''}`}></span>
                {following ? 'Following' : 'Follow'}
              </button>
            )}
            {completed && (
              <span style={{ fontSize: '0.8em', color: 'var(--text-dim)', padding: '0.25rem 0.5rem' }}>Completed</span>
            )}
            <button
              className={`btn btn-sm${copied ? ' copied' : ''}`}
              onClick={handleCopy}
            >
              {copied ? 'Copied!' : 'Copy'}
            </button>
            <button className="btn btn-sm" onClick={onClose}>Close</button>
          </div>
        </div>
        <div className="viewer-body-wrap">
          <div
            ref={bodyRef}
            className={`viewer-body${!following ? ' log-paused' : ''}`}
            id="log-viewer-content"
          >
            {loading ? 'Loading...' : (content || 'No log output')}
          </div>
        </div>
        {totalLines !== undefined && (
          <div style={{
            padding: '0.4rem 1.25rem',
            borderTop: '1px solid var(--border)',
            fontSize: '0.75rem',
            color: 'var(--text-dim)',
          }}>
            Total lines: {totalLines}
          </div>
        )}
      </div>
    </div>
  );
}
