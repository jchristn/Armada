import { useState, useRef, useEffect, useCallback, useLayoutEffect } from 'react';
import { createPortal } from 'react-dom';

export interface ActionMenuItem {
  label: string;
  onClick: () => void;
  danger?: boolean;
  disabled?: boolean;
}

interface ActionMenuProps {
  items: ActionMenuItem[];
  id: string;
}

export default function ActionMenu({ items, id }: ActionMenuProps) {
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

  // Close on escape
  useEffect(() => {
    if (!open) return;
    const handleKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') setOpen(false);
    };
    document.addEventListener('keydown', handleKey);
    return () => document.removeEventListener('keydown', handleKey);
  }, [open]);

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
      <button className="action-menu-btn" onClick={handleToggle} title="Actions">
        &#8942;
      </button>
      {open && createPortal(
        <div
          ref={dropdownRef}
          className={`action-menu-dropdown action-menu-dropdown-fixed${dropUp ? ' drop-up' : ''}`}
          style={{ top: `${menuStyle.top}px`, left: `${menuStyle.left}px`, minWidth: `${menuStyle.minWidth}px` }}
          onClick={(e) => e.stopPropagation()}
          onMouseDown={(e) => e.stopPropagation()}
        >
          {items.map((item, i) => (
            <button
              key={i}
              className={`action-menu-item${item.danger ? ' danger' : ''}`}
              disabled={item.disabled}
              onMouseDown={(e) => e.stopPropagation()}
              onClick={(e) => {
                e.stopPropagation();
                setOpen(false);
                item.onClick();
              }}
            >
              {item.label}
            </button>
          ))}
        </div>,
        document.body
      )}
    </div>
  );
}
