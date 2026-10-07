import { useCallback, useEffect, useId, useLayoutEffect, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import { useLocale } from '../../context/LocaleContext';

export interface ColumnChooserOption {
  key: string;
  /** Already-translated column label. */
  label: string;
  /** Always shown: rendered checked and not toggleable. */
  required?: boolean;
}

interface ColumnChooserProps {
  options: ColumnChooserOption[];
  isVisible: (key: string) => boolean;
  onToggle: (key: string) => void;
  onReset: () => void;
  /** True when the current choice is already the default (disables Reset). */
  isDefault: boolean;
}

const MENU_WIDTH = 230;

/**
 * Menu button that shows or hides a table's columns. A `role="menu"` of `menuitemcheckbox` items, keyboard
 * operable like ActionMenu: focus moves into the menu when it opens, the arrow keys, Home and End move between
 * items, Space or Enter toggles one, Escape or Tab closes it and returns focus to the button. Required columns are
 * listed checked and disabled. Rendered in a portal so table scroll areas and dialogs never clip it.
 */
export default function ColumnChooser({ options, isVisible, onToggle, onReset, isDefault }: ColumnChooserProps) {
  const { t } = useLocale();
  const [open, setOpen] = useState(false);
  const [position, setPosition] = useState<{ top: number; left: number }>({ top: 0, left: 0 });
  const triggerRef = useRef<HTMLButtonElement>(null);
  const menuRef = useRef<HTMLDivElement>(null);
  const menuId = useId();

  const hiddenCount = options.filter((o) => !o.required && !isVisible(o.key)).length;

  const place = useCallback(() => {
    const rect = triggerRef.current?.getBoundingClientRect();
    if (!rect) return;
    const left = Math.max(8, Math.min(rect.right - MENU_WIDTH, window.innerWidth - MENU_WIDTH - 8));
    setPosition({ top: rect.bottom + 4, left });
  }, []);

  const close = useCallback((restoreFocus: boolean) => {
    setOpen(false);
    if (restoreFocus) triggerRef.current?.focus();
  }, []);

  const items = useCallback(
    () => Array.from(menuRef.current?.querySelectorAll<HTMLElement>('[data-chooser-item]') ?? []),
    [],
  );

  useLayoutEffect(() => {
    if (open) place();
  }, [open, place]);

  useEffect(() => {
    if (!open) return;
    // Start on the first column the user can change (disabled items stay reachable with the arrow keys).
    const list = items();
    (list.find((el) => el.getAttribute('aria-disabled') !== 'true') ?? list[0])?.focus();
  }, [open, items]);

  useEffect(() => {
    if (!open) return undefined;
    const onPointer = (event: MouseEvent) => {
      const target = event.target as Node;
      if (triggerRef.current?.contains(target) || menuRef.current?.contains(target)) return;
      setOpen(false);
    };
    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        // Capture phase: close only this menu, not a dialog it sits in.
        event.stopPropagation();
        event.preventDefault();
        close(true);
        return;
      }
      if (!menuRef.current?.contains(document.activeElement)) return;
      const list = items();
      const index = list.indexOf(document.activeElement as HTMLElement);
      if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
        event.preventDefault();
        const next = event.key === 'ArrowDown' ? (index + 1) % list.length : (index - 1 + list.length) % list.length;
        list[next]?.focus();
      } else if (event.key === 'Home' || event.key === 'End') {
        event.preventDefault();
        list[event.key === 'Home' ? 0 : list.length - 1]?.focus();
      } else if (event.key === 'Tab') {
        event.preventDefault();
        close(true);
      }
    };
    const onViewportChange = (event: Event) => {
      if (event.type === 'scroll' && menuRef.current && event.target instanceof Node && menuRef.current.contains(event.target)) return;
      setOpen(false);
    };
    document.addEventListener('mousedown', onPointer, true);
    document.addEventListener('keydown', onKey, true);
    window.addEventListener('resize', onViewportChange);
    window.addEventListener('scroll', onViewportChange, true);
    return () => {
      document.removeEventListener('mousedown', onPointer, true);
      document.removeEventListener('keydown', onKey, true);
      window.removeEventListener('resize', onViewportChange);
      window.removeEventListener('scroll', onViewportChange, true);
    };
  }, [open, items, close]);

  const label = hiddenCount > 0 ? t('Columns ({{count}} hidden)', { count: hiddenCount }) : t('Columns');

  return (
    <>
      <button
        ref={triggerRef}
        type="button"
        className={`btn btn-sm column-chooser-btn${hiddenCount > 0 ? ' column-chooser-active' : ''}`}
        aria-haspopup="menu"
        aria-expanded={open}
        aria-controls={open ? menuId : undefined}
        title={t('Choose visible columns')}
        onClick={() => setOpen((v) => !v)}
      >
        <span>{label}</span>
        <span aria-hidden="true" className="column-chooser-caret">{'\u25BE'}</span>
      </button>
      {open && createPortal(
        <div
          ref={menuRef}
          id={menuId}
          role="menu"
          aria-label={t('Choose visible columns')}
          className="action-menu-dropdown action-menu-dropdown-fixed column-chooser-menu"
          style={{ top: `${position.top}px`, left: `${position.left}px`, width: `${MENU_WIDTH}px` }}
          onClick={(e) => e.stopPropagation()}
          onMouseDown={(e) => e.stopPropagation()}
        >
          {options.map((option) => {
            const checked = option.required || isVisible(option.key);
            const toggle = () => { if (!option.required) onToggle(option.key); };
            return (
              <div
                key={option.key}
                data-chooser-item=""
                role="menuitemcheckbox"
                aria-checked={checked}
                aria-disabled={option.required ? true : undefined}
                tabIndex={-1}
                className={`column-chooser-item${option.required ? ' column-chooser-locked' : ''}`}
                title={option.required ? t('Always shown') : undefined}
                onClick={toggle}
                onKeyDown={(e) => {
                  if (e.key === ' ' || e.key === 'Enter') {
                    e.preventDefault();
                    toggle();
                  }
                }}
              >
                <span aria-hidden="true" className="column-chooser-check">{checked ? '\u2611' : '\u2610'}</span>
                <span className="column-chooser-label">{option.label}</span>
                {option.required && <span className="column-chooser-note text-dim">{t('Always shown')}</span>}
              </div>
            );
          })}
          <div role="separator" className="column-chooser-separator" />
          <div
            data-chooser-item=""
            role="menuitem"
            tabIndex={-1}
            aria-disabled={isDefault ? true : undefined}
            className="column-chooser-item column-chooser-reset"
            onClick={() => { if (!isDefault) onReset(); }}
            onKeyDown={(e) => {
              if ((e.key === ' ' || e.key === 'Enter') && !isDefault) {
                e.preventDefault();
                onReset();
              }
            }}
          >
            {t('Reset to default')}
          </div>
        </div>,
        document.body,
      )}
    </>
  );
}
