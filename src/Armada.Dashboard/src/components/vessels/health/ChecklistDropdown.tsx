import { useEffect, useId, useRef, useState, type ReactNode } from 'react';
import { useLocale } from '../../../context/LocaleContext';

export interface ChecklistOption {
  value: string;
  label: string;
  /** Rendered checked and disabled (for example pinned columns). */
  locked?: boolean;
}

interface ChecklistDropdownProps {
  /** Already-translated button label. */
  label: string;
  /** Already-translated summary of the current selection shown after the label (optional). */
  summary?: string;
  options: ChecklistOption[];
  selected: string[];
  onToggle: (value: string) => void;
  onClear?: () => void;
  /** Already-translated accessible name for the popover group. */
  ariaLabel: string;
  footer?: ReactNode;
  align?: 'left' | 'right';
}

/**
 * Compact multi-select: a button that opens a small checkbox popover. Used for the status filters and
 * the column chooser. Closes on outside click and Escape; the popover lives outside table scroll areas.
 */
export default function ChecklistDropdown({ label, summary, options, selected, onToggle, onClear, ariaLabel, footer, align = 'left' }: ChecklistDropdownProps) {
  const { t } = useLocale();
  const [open, setOpen] = useState(false);
  const wrapRef = useRef<HTMLDivElement>(null);
  const popoverId = useId();

  useEffect(() => {
    if (!open) return undefined;
    const onDocClick = (event: MouseEvent) => {
      if (wrapRef.current && !wrapRef.current.contains(event.target as Node)) setOpen(false);
    };
    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        event.stopPropagation();
        setOpen(false);
      }
    };
    document.addEventListener('mousedown', onDocClick);
    document.addEventListener('keydown', onKey);
    return () => {
      document.removeEventListener('mousedown', onDocClick);
      document.removeEventListener('keydown', onKey);
    };
  }, [open]);

  const active = selected.length > 0;

  return (
    <div className="vh-checklist" ref={wrapRef}>
      <button
        type="button"
        className={`btn btn-sm vh-checklist-btn${active ? ' vh-checklist-active' : ''}`}
        aria-haspopup="true"
        aria-expanded={open}
        aria-controls={popoverId}
        onClick={() => setOpen((v) => !v)}
      >
        <span>{label}</span>
        {summary && <span className="vh-checklist-summary">{summary}</span>}
        <span aria-hidden="true" className="vh-caret">{'\u25BE'}</span>
      </button>
      {open && (
        <div id={popoverId} className={`vh-checklist-popover${align === 'right' ? ' vh-align-right' : ''}`} role="group" aria-label={ariaLabel}>
          {options.map((option) => {
            const checked = option.locked || selected.includes(option.value);
            return (
              <label key={option.value} className={`vh-checklist-item${option.locked ? ' vh-locked' : ''}`}>
                <input
                  type="checkbox"
                  checked={checked}
                  disabled={option.locked}
                  onChange={() => onToggle(option.value)}
                />
                <span>{option.label}</span>
              </label>
            );
          })}
          {(onClear || footer) && (
            <div className="vh-checklist-footer">
              {footer}
              {onClear && (
                <button type="button" className="btn btn-sm" onClick={() => onClear()} disabled={!active}>
                  {t('Clear')}
                </button>
              )}
            </div>
          )}
        </div>
      )}
    </div>
  );
}
