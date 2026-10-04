import { useLayoutEffect, useRef, type RefObject } from 'react';

/**
 * Shared modal accessibility for the dashboard.
 *
 * Every open modal is registered on one stack. Only the top dialog owns the keyboard: Tab and Shift+Tab cycle
 * inside it, Escape closes it, and when it closes focus returns to the element that was focused before it
 * opened. Components use {@link useDialog} (or the older {@link useFocusTrap} wrapper). Legacy markup that renders
 * `<div className="modal-overlay"><div className="modal">...` is upgraded by {@link installDialogEnhancer}, which
 * watches the DOM, gives the panel `role="dialog"`, `aria-modal="true"` and an `aria-labelledby` pointing at its
 * first heading, and registers it on the same stack (Escape clicks the overlay, which is how those modals close).
 */

export const FOCUSABLE_SELECTOR = [
  'a[href]',
  'button:not([disabled])',
  'input:not([disabled]):not([type="hidden"])',
  'select:not([disabled])',
  'textarea:not([disabled])',
  'summary',
  '[tabindex]:not([tabindex="-1"])',
  '[contenteditable="true"]',
].join(', ');

/** Overlays rendered by legacy modal markup; the panel is the overlay's first element child. */
export const LEGACY_OVERLAY_SELECTOR = [
  '.modal-overlay:not(.workspace-loading-overlay)',
  '.diff-modal-overlay',
  '.json-viewer-overlay',
  '.viewer-overlay',
  '.wizard-overlay',
].join(', ');

/** Marks a panel whose dialog semantics are owned by a component (the enhancer leaves it alone). */
export const MANAGED_ATTR = 'data-dialog-managed';

interface DialogEntry {
  panel: HTMLElement;
  onEscape: () => void;
  escapeEnabled: () => boolean;
  restoreTo: HTMLElement | null;
}

const _Stack: DialogEntry[] = [];
let _ListenerInstalled = false;
let _TitleCounter = 0;

function isVisible(el: HTMLElement): boolean {
  if (el === document.activeElement) return true;
  // jsdom has no layout; treat everything as visible there.
  if (typeof navigator !== 'undefined' && /jsdom/i.test(navigator.userAgent)) return !el.closest('[hidden]');
  return el.offsetParent !== null || getComputedStyle(el).position === 'fixed';
}

/** Focusable, visible descendants of a dialog panel in DOM order. */
export function getFocusable(panel: HTMLElement): HTMLElement[] {
  return Array.from(panel.querySelectorAll<HTMLElement>(FOCUSABLE_SELECTOR)).filter((el) => isVisible(el));
}

function onDocumentKeyDown(event: KeyboardEvent): void {
  const top = _Stack[_Stack.length - 1];
  if (!top) return;
  // A popup menu (rendered in a portal) owns the keyboard while focus is inside it.
  const target = event.target as HTMLElement | null;
  if (target && target.closest && target.closest('[role="menu"]')) return;
  if (event.key === 'Escape') {
    if (!top.escapeEnabled()) return;
    event.preventDefault();
    event.stopPropagation();
    top.onEscape();
    return;
  }
  if (event.key !== 'Tab') return;
  const focusable = getFocusable(top.panel);
  const current = document.activeElement as HTMLElement | null;
  if (focusable.length === 0) {
    event.preventDefault();
    top.panel.focus();
    return;
  }
  const first = focusable[0];
  const last = focusable[focusable.length - 1];
  if (event.shiftKey) {
    if (current === first || !current || !top.panel.contains(current) || current === top.panel) {
      event.preventDefault();
      last.focus();
    }
  } else if (current === last || !current || !top.panel.contains(current)) {
    event.preventDefault();
    first.focus();
  }
}

function ensureListener(): void {
  if (_ListenerInstalled || typeof document === 'undefined') return;
  document.addEventListener('keydown', onDocumentKeyDown, true);
  _ListenerInstalled = true;
}

/** Give a panel an accessible name from its first heading when it has none. */
export function labelFromHeading(panel: HTMLElement): void {
  if (panel.getAttribute('aria-labelledby') || panel.getAttribute('aria-label')) return;
  const heading = panel.querySelector<HTMLElement>('h1, h2, h3, h4, [data-dialog-title]');
  if (!heading) return;
  if (!heading.id) {
    _TitleCounter += 1;
    heading.id = `dialog-title-${_TitleCounter}`;
  }
  panel.setAttribute('aria-labelledby', heading.id);
}

function focusInitial(panel: HTMLElement): void {
  const active = document.activeElement as HTMLElement | null;
  if (active && active !== document.body && panel.contains(active)) return; // autoFocus already placed it
  const focusable = getFocusable(panel);
  const field = focusable.find((el) => el.matches('input, select, textarea'));
  const target = field ?? focusable.find((el) => !el.classList.contains('dialog-shell-close')) ?? focusable[0];
  if (target) {
    target.focus();
    return;
  }
  if (!panel.hasAttribute('tabindex')) panel.setAttribute('tabindex', '-1');
  panel.focus();
}

/**
 * Register an open dialog panel. Returns a function that unregisters it and returns focus to the element that was
 * focused before it opened (when that element is still in the document).
 */
export function registerDialog(panel: HTMLElement, onEscape: () => void, escapeEnabled: () => boolean = () => true): () => void {
  ensureListener();
  const previous = document.activeElement as HTMLElement | null;
  const entry: DialogEntry = {
    panel,
    onEscape,
    escapeEnabled,
    restoreTo: previous && previous !== document.body && !panel.contains(previous) ? previous : null,
  };
  _Stack.push(entry);
  focusInitial(panel);
  let done = false;
  return () => {
    if (done) return;
    done = true;
    const index = _Stack.indexOf(entry);
    const wasTop = index === _Stack.length - 1;
    if (index >= 0) _Stack.splice(index, 1);
    if (!wasTop) return;
    const active = document.activeElement as HTMLElement | null;
    const focusLost = !active || active === document.body || panel.contains(active) || !active.isConnected;
    if (!focusLost) return;
    const restore = entry.restoreTo && entry.restoreTo.isConnected ? entry.restoreTo : null;
    const next = _Stack[_Stack.length - 1];
    if (restore && (!next || next.panel.contains(restore))) restore.focus();
    else if (next) focusInitial(next.panel);
  };
}

/** Number of dialogs currently registered (for tests). */
export function openDialogCount(): number {
  return _Stack.length;
}

/**
 * Dialog behavior for a component-owned panel: role/aria-modal must be in the component's markup; this hook adds
 * the focus trap, Escape handling, initial focus and focus return. `escapeEnabled` false keeps the dialog open on
 * Escape (for example while a request is in flight).
 */
export function useDialog(panelRef: RefObject<HTMLElement | null>, open: boolean, onEscape: () => void, escapeEnabled = true): void {
  const escapeRef = useRef(onEscape);
  escapeRef.current = onEscape;
  const enabledRef = useRef(escapeEnabled);
  enabledRef.current = escapeEnabled;

  // Layout effect: runs in the same task as the DOM commit, before the enhancer's MutationObserver callback, so the
  // panel is marked as component-managed before the enhancer could claim it.
  useLayoutEffect(() => {
    if (!open) return undefined;
    const panel = panelRef.current;
    if (!panel) return undefined;
    panel.setAttribute(MANAGED_ATTR, 'true');
    return registerDialog(panel, () => escapeRef.current(), () => enabledRef.current);
  }, [open, panelRef]);
}

/**
 * Backwards-compatible alias used by drawers and the vessel health modal. Same behavior as {@link useDialog}.
 */
export function useFocusTrap(containerRef: RefObject<HTMLElement | null>, active: boolean, onEscape: () => void, escapeEnabled = true): void {
  useDialog(containerRef, active, onEscape, escapeEnabled);
}

const _Enhanced = new WeakMap<Element, () => void>();

function enhanceOverlay(overlay: HTMLElement): void {
  if (_Enhanced.has(overlay)) return;
  const first = (overlay.querySelector<HTMLElement>('[role="dialog"]') ?? overlay.firstElementChild) as HTMLElement | null;
  if (!first || first.hasAttribute(MANAGED_ATTR) || first.closest(`[${MANAGED_ATTR}]`)) return;
  // role="dialog" is not allowed on a <form>; when the modal panel is a form, the overlay carries the dialog role.
  const panel = first.getAttribute('role') === 'dialog' || /^(DIV|SECTION|ASIDE|ARTICLE)$/.test(first.tagName) ? first : overlay;
  if (!panel.getAttribute('role')) panel.setAttribute('role', 'dialog');
  if (!panel.getAttribute('aria-modal')) panel.setAttribute('aria-modal', 'true');
  labelFromHeading(panel);
  if (!panel.hasAttribute('tabindex')) panel.setAttribute('tabindex', '-1');
  const unregister = registerDialog(panel, () => {
    // Legacy modals close (and apply their own busy guards) in the overlay's click handler.
    overlay.click();
  });
  _Enhanced.set(overlay, unregister);
}

function scan(root: ParentNode): void {
  if (root instanceof HTMLElement && root.matches(LEGACY_OVERLAY_SELECTOR)) enhanceOverlay(root);
  root.querySelectorAll<HTMLElement>(LEGACY_OVERLAY_SELECTOR).forEach(enhanceOverlay);
}

/**
 * Upgrade legacy modal markup anywhere under `root` (default `document.body`) for as long as the returned function
 * is not called. Safe to call more than once; each call returns its own disconnect function.
 */
export function installDialogEnhancer(root: HTMLElement = document.body): () => void {
  ensureListener();
  scan(root);
  const tracked = new Set<HTMLElement>();
  const observer = new MutationObserver((records) => {
    for (const record of records) {
      record.addedNodes.forEach((node) => {
        if (node instanceof HTMLElement) scan(node);
      });
      if (record.removedNodes.length > 0 || record.addedNodes.length > 0) {
        // Re-label a panel whose heading arrived after the panel itself (async content).
        root.querySelectorAll<HTMLElement>(LEGACY_OVERLAY_SELECTOR).forEach((overlay) => {
          tracked.add(overlay);
          const panel = overlay.querySelector<HTMLElement>('[role="dialog"]');
          if (panel && !panel.hasAttribute(MANAGED_ATTR)) labelFromHeading(panel);
        });
      }
    }
    for (const overlay of Array.from(tracked)) {
      if (!overlay.isConnected) {
        tracked.delete(overlay);
        const unregister = _Enhanced.get(overlay);
        if (unregister) {
          _Enhanced.delete(overlay);
          unregister();
        }
      }
    }
  });
  observer.observe(root, { childList: true, subtree: true });
  root.querySelectorAll<HTMLElement>(LEGACY_OVERLAY_SELECTOR).forEach((o) => tracked.add(o));
  return () => observer.disconnect();
}
