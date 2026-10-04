import { useState, useRef, useEffect, useCallback, useLayoutEffect } from 'react';
import { createPortal } from 'react-dom';
import { useLocale } from '../../context/LocaleContext';

export interface ActionMenuItem {
  label: string;
  onClick: () => void;
  danger?: boolean;
  disabled?: boolean;
}

interface ActionMenuProps {
  items: ActionMenuItem[];
  id: string;
  /** Accessible name and tooltip for the trigger (already localized); defaults to "Actions". */
  triggerLabel?: string;
}

export default function ActionMenu({ items, id, triggerLabel }: ActionMenuProps) {
  const { t } = useLocale();
  const [open, setOpen] = useState(false);
  const [dropUp, setDropUp] = useState(false);
  const [menuStyle, setMenuStyle] = useState<{ top: number; left: number; minWidth: number }>({
    top: 0,
    left: 0,
    minWidth: 150,
  });
  const wrapRef = useRef<HTMLDivElement>(null);
  const dropdownRef = useRef<HTMLDivElement>(null);

  const updateMenuPosition = useCallback(() => {
    const btn = wrapRef.current;
    if (!btn) return;

    const rect = btn.getBoundingClientRect();
    const estimatedMenuHeight = Math.max(items.length * 36 + 12, 80);
    const spaceBelow = window.innerHeight - rect.bottom;
    const shouldDropUp = spaceBelow < estimatedMenuHeight && rect.top > spaceBelow;
    const minWidth = 150;
    const menuWidth = Math.max(rect.width, minWidth);
    const left = Math.max(8, Math.min(rect.right - menuWidth, window.innerWidth - menuWidth - 8));
    const top = shouldDropUp
      ? Math.max(8, rect.top - estimatedMenuHeight - 4)
      : Math.min(window.innerHeight - estimatedMenuHeight - 8, rect.bottom + 4);

    setDropUp(shouldDropUp);
    setMenuStyle({ top, left, minWidth: menuWidth });
  }, [items.length]);

  const handleToggle = useCallback((e: React.MouseEvent) => {
    e.stopPropagation();
    if (!open) {
      updateMenuPosition();
      setOpen(true);
      return;
    }
    setOpen(false);
  }, [open, updateMenuPosition]);

  useLayoutEffect(() => {
    if (!open) return;
    updateMenuPosition();
  }, [open, updateMenuPosition]);

  // Close on click outside
  useEffect(() => {
    if (!open) return;
    const handleClick = (e: MouseEvent) => {
      const target = e.target as Node;
      const clickedTrigger = wrapRef.current?.contains(target);
      const clickedDropdown = dropdownRef.current?.contains(target);
      if (!clickedTrigger && !clickedDropdown) {
        setOpen(false);
      }
    };
    document.addEventListener('click', handleClick, true);
    return () => document.removeEventListener('click', handleClick, true);
  }, [open]);

  // Keyboard: the menu renders in a portal at the end of <body>, so move focus into it when it opens, let the arrow
  // keys, Home and End move between items, and hand focus back to the trigger on Escape or Tab.
  const triggerRef = useRef<HTMLButtonElement>(null);
  const menuItems = useCallback(
    () => Array.from(dropdownRef.current?.querySelectorAll<HTMLButtonElement>('.action-menu-item:not(:disabled)') ?? []),
    [],
  );
  useEffect(() => {
    if (!open) return;
    const first = menuItems()[0];
    if (first) first.focus();
  }, [open, menuItems]);
  useEffect(() => {
    if (!open) return;
    const handleKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        // Capture phase: close only the menu, not a dialog the menu sits in.
        e.stopPropagation();
        e.preventDefault();
        setOpen(false);
        triggerRef.current?.focus();
        return;
      }
      if (!dropdownRef.current?.contains(document.activeElement)) return;
      const list = menuItems();
      const index = list.indexOf(document.activeElement as HTMLButtonElement);
      if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
        e.preventDefault();
        const next = e.key === 'ArrowDown' ? (index + 1) % list.length : (index - 1 + list.length) % list.length;
        list[next]?.focus();
      } else if (e.key === 'Home' || e.key === 'End') {
        e.preventDefault();
        list[e.key === 'Home' ? 0 : list.length - 1]?.focus();
      } else if (e.key === 'Tab') {
        e.preventDefault();
        setOpen(false);
        triggerRef.current?.focus();
      }
    };
    document.addEventListener('keydown', handleKey, true);
    return () => document.removeEventListener('keydown', handleKey, true);
  }, [open, menuItems]);

  useEffect(() => {
    if (!open) return;
    const handleWindowChange = () => setOpen(false);
    window.addEventListener('resize', handleWindowChange);
    window.addEventListener('scroll', handleWindowChange, true);
    return () => {
      window.removeEventListener('resize', handleWindowChange);
      window.removeEventListener('scroll', handleWindowChange, true);
    };
  }, [open]);

  if (items.length === 0) return null;

  return (
    <div className="action-menu-wrap" ref={wrapRef} data-menu-id={id}>
      <button ref={triggerRef} type="button" className="action-menu-btn" onClick={handleToggle} title={triggerLabel ?? t('Actions')} aria-label={triggerLabel ?? t('Actions')} aria-haspopup="menu" aria-expanded={open}>
        &#8942;
      </button>
      {open && createPortal(
        <div
          ref={dropdownRef}
          className={`action-menu-dropdown action-menu-dropdown-fixed${dropUp ? ' drop-up' : ''}`}
          role="menu"
          aria-label={triggerLabel ?? t('Actions')}
          style={{ top: `${menuStyle.top}px`, left: `${menuStyle.left}px`, minWidth: `${menuStyle.minWidth}px` }}
          onClick={(e) => e.stopPropagation()}
          onMouseDown={(e) => e.stopPropagation()}
        >
          {items.map((item, i) => (
            <button
              key={i}
              type="button"
              role="menuitem"
              className={`action-menu-item${item.danger ? ' danger' : ''}`}
              disabled={item.disabled}
              onMouseDown={(e) => e.stopPropagation()}
              onClick={(e) => {
                e.stopPropagation();
                setOpen(false);
                // Return focus to the trigger first so a dialog the item opens restores focus to it on close.
                triggerRef.current?.focus();
                item.onClick();
              }}
            >
              {t(item.label)}
            </button>
          ))}
        </div>,
        document.body
      )}
    </div>
  );
}
