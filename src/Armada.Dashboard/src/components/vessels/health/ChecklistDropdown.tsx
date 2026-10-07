import { useEffect, useId, useRef, useState } from 'react';
import { useLocale } from '../../../context/LocaleContext';

export interface ChecklistOption {
  value: string;
  label: string;
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
}

/**
 * Compact multi-select: a button that opens a small checkbox popover. Used for the status filters (the column
 * chooser is the shared DataTable's). Closes on outside click and Escape; the popover lives outside table scroll areas.
 */
export default function ChecklistDropdown({ label, summary, options, selected, onToggle, onClear, ariaLabel }: ChecklistDropdownProps) {
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
        <div id={popoverId} className="vh-checklist-popover" role="group" aria-label={ariaLabel}>
          {options.map((option) => {
            const checked = selected.includes(option.value);
            return (
              <label key={option.value} className="vh-checklist-item">
                <input
                  type="checkbox"
                  checked={checked}
                  onChange={() => onToggle(option.value)}
                />
                <span>{option.label}</span>
              </label>
            );
          })}
          {onClear && (
            <div className="vh-checklist-footer">
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
