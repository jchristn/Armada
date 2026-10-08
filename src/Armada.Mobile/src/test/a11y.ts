/**
 * Automated accessibility audit of a rendered React Native tree (the host elements React Native Testing Library
 * exposes). It finds what a screen reader user cannot use, without a device:
 *
 * - touchable-role: a touchable (Pressable, Touchable*, Text with onPress) without an accessibility role, so
 *   VoiceOver and TalkBack do not say what it does.
 * - touchable-name: a touchable with no accessible name (no label and no visible text), typically an icon-only
 *   button.
 * - input-label: a text input without an accessible name (placeholder text is not a name).
 * - switch-label: a switch without an accessible name.
 * - image-label: an image that screen readers reach (not hidden) without a label.
 * - nested-interactive: a touchable or input inside an element that is itself one accessibility element
 *   (`accessible`): iOS makes the inner control unreachable.
 * - unreachable-actions: accessibility actions (swipe or menu actions for screen readers) on an element that is not
 *   itself an accessibility element, so VoiceOver and TalkBack never offer them.
 * - summary-incomplete: an element read as one (accessible, with its own label) that shows text its label and
 *   value leave out, for example a list row whose status badge is never spoken.
 *
 * Hidden subtrees (accessibilityElementsHidden, importantForAccessibility no-hide-descendants, aria-hidden) are
 * skipped, as screen readers skip them.
 *
 *   expectAccessible(screen.root);
 */

export type A11yRule = 'touchable-role' | 'touchable-name' | 'input-label' | 'switch-label' | 'image-label' | 'nested-interactive' | 'summary-incomplete' | 'unreachable-actions';

export interface A11yIssue {
  rule: A11yRule;
  /** testID of the element or of its nearest ancestor with one, or the element type. */
  where: string;
  detail: string;
}

/** A host element as RNTL exposes it (the test renderer's instance shape, without depending on its exact type). */
export interface HostNode {
  type: unknown;
  props: Record<string, unknown>;
  children: (HostNode | string)[];
  parent: HostNode | null;
}

function isHidden(props: Record<string, unknown>): boolean {
  return props.accessibilityElementsHidden === true
    || props.importantForAccessibility === 'no-hide-descendants'
    || props['aria-hidden'] === true;
}

function typeName(node: HostNode): string {
  return typeof node.type === 'string' ? node.type : 'Component';
}

/** True for host elements a finger can activate. */
export function isTouchable(node: HostNode): boolean {
  const type = typeName(node);
  if (type === 'Text') return typeof node.props.onPress === 'function' || typeof node.props.onLongPress === 'function';
  if (type !== 'View') return false;
  // Pressable and the Touchable* components render a View with a click handler and the responder props.
  return typeof node.props.onClick === 'function' && typeof node.props.onResponderRelease === 'function';
}

function isInput(node: HostNode): boolean {
  return typeName(node) === 'TextInput';
}

function isSwitch(node: HostNode): boolean {
  const type = typeName(node);
  return type === 'RCTSwitch' || type === 'AndroidSwitch';
}

function role(props: Record<string, unknown>): string | null {
  const value = props.accessibilityRole ?? props.role;
  return typeof value === 'string' && value !== 'none' ? value : null;
}

function label(props: Record<string, unknown>): string {
  const value = props.accessibilityLabel ?? props['aria-label'];
  return typeof value === 'string' ? value.trim() : '';
}

function labelledBy(props: Record<string, unknown>): boolean {
  const value = props.accessibilityLabelledBy ?? props['aria-labelledby'];
  return typeof value === 'string' ? value.length > 0 : Array.isArray(value) && value.length > 0;
}

// Icon fonts draw glyphs from the Unicode private use area; a glyph is not a name.
const PRIVATE_USE = /[\uE000-\uF8FF]/g;

/** The text a screen reader would read from a subtree's visible text (hidden subtrees excluded). */
export function spokenText(node: HostNode | string): string {
  if (typeof node === 'string') return node.replace(PRIVATE_USE, '');
  if (isHidden(node.props)) return '';
  const own = label(node.props);
  // A Text is its own accessibility element, so its label replaces its text (as for an accessible View).
  if (own && (node.props.accessible === true || typeName(node) === 'Text')) return own;
  return node.children.map(spokenText).join(' ').replace(/\s+/g, ' ').trim();
}

function valueText(props: Record<string, unknown>): string {
  const value = props.accessibilityValue as { text?: unknown } | undefined;
  const aria = props['aria-valuetext'];
  return [typeof value?.text === 'string' ? value.text : '', typeof aria === 'string' ? aria : ''].join(' ');
}

const normalize = (text: string) => text.replace(PRIVATE_USE, '').replace(/\s+/g, ' ').trim().toLowerCase();

/** The pieces a sighted user reads inside an element: each Text's own text and each labelled accessible child. */
function shownPieces(node: HostNode): string[] {
  const out: string[] = [];
  const visit = (n: HostNode | string): void => {
    if (typeof n === 'string') return;
    if (isHidden(n.props)) return;
    if (n.props.accessible === true && label(n.props)) {
      // A labelled child (a progress bar, a badge) counts as read when its label or its value text is.
      out.push(`${label(n.props)}\u0000${valueText(n.props).trim()}`);
      return;
    }
    if (typeName(n) === 'Text') {
      const own = spokenText(n);
      if (own) out.push(own);
      return;
    }
    n.children.forEach(visit);
  };
  node.children.forEach(visit);
  return out.filter((piece) => /[\p{L}\p{N}]/u.test(piece.split('\u0000')[0]));
}

function where(node: HostNode): string {
  let at: HostNode | null = node;
  while (at) {
    const id = at.props.testID;
    if (typeof id === 'string' && id) return at === node ? id : `${typeName(node)} in ${id}`;
    at = at.parent;
  }
  return typeName(node);
}

/** Audits a rendered tree; returns every issue found (an empty list means it passed). */
export function auditAccessibility(root: object | null): A11yIssue[] {
  const issues: A11yIssue[] = [];
  const visit = (node: HostNode | string, accessibleAncestor: HostNode | null): void => {
    if (typeof node === 'string') return;
    if (isHidden(node.props)) return;
    const touchable = isTouchable(node);
    const input = isInput(node);
    const toggle = isSwitch(node);
    if ((touchable || input || toggle) && accessibleAncestor) {
      issues.push({ rule: 'nested-interactive', where: where(node), detail: `inside the accessibility element ${where(accessibleAncestor)}` });
    }
    if (touchable) {
      if (!role(node.props)) issues.push({ rule: 'touchable-role', where: where(node), detail: `"${spokenText(node).slice(0, 60)}" has no accessibilityRole` });
      if (!label(node.props) && !spokenText(node)) issues.push({ rule: 'touchable-name', where: where(node), detail: 'no accessibilityLabel and no text' });
    }
    if (input && !label(node.props) && !labelledBy(node.props)) {
      issues.push({ rule: 'input-label', where: where(node), detail: 'no accessibilityLabel' });
    }
    if (toggle && !label(node.props) && !labelledBy(node.props)) {
      issues.push({ rule: 'switch-label', where: where(node), detail: 'no accessibilityLabel' });
    }
    if (node.props.accessible === true && label(node.props) && typeName(node) !== 'Text' && !input) {
      const spoken = normalize(`${label(node.props)} ${valueText(node.props)}`);
      for (const piece of shownPieces(node)) {
        const alternatives = piece.split('\u0000').map(normalize).filter(Boolean);
        if (!alternatives.some((alt) => spoken.includes(alt))) {
          issues.push({ rule: 'summary-incomplete', where: where(node), detail: `shows "${alternatives[0].slice(0, 60)}" but reads "${label(node.props).slice(0, 80)}"` });
        }
      }
    }
    const actions = node.props.accessibilityActions;
    if (Array.isArray(actions) && actions.length > 0 && node.props.accessible !== true) {
      issues.push({ rule: 'unreachable-actions', where: where(node), detail: `${actions.length} action(s) on an element screen readers do not focus` });
    }
    if (typeName(node) === 'Image' && node.props.accessible !== false && node.props.importantForAccessibility !== 'no' && !label(node.props)) {
      issues.push({ rule: 'image-label', where: where(node), detail: 'not hidden and no accessibilityLabel' });
    }
    // Text groups its own nested Text (links inside a paragraph are reachable as links); only Views hide children.
    const groups = node.props.accessible === true && typeName(node) !== 'Text';
    const nextAncestor = accessibleAncestor ?? (groups ? node : null);
    for (const child of node.children) visit(child, nextAncestor);
  };
  if (root) visit(root as HostNode, null);
  return issues;
}

/** Formats issues one per line for a failure message. */
export function formatIssues(issues: A11yIssue[]): string {
  return issues.map((i) => `${i.rule}: ${i.where}: ${i.detail}`).join('\n');
}

/** Fails the test with every accessibility issue found in the tree. */
export function expectAccessible(root: object | null): void {
  const issues = auditAccessibility(root);
  if (issues.length > 0) throw new Error(`Accessibility issues:\n${formatIssues(issues)}`);
}

/**
 * Runs a row's screen-reader action the way VoiceOver and TalkBack do: on the accessibility element inside `row`
 * (a SwipeRow wrapper or the row itself) that offers it. Fails when no focusable element offers the action.
 */
export function rowActionTarget<T extends object>(row: T, actionName: string): T {
  const offers = (n: HostNode) => n.props.accessible === true && Array.isArray(n.props.accessibilityActions)
    && (n.props.accessibilityActions as { name: string }[]).some((a) => a.name === actionName);
  const queue: (HostNode | string)[] = [row as HostNode];
  while (queue.length > 0) {
    const n = queue.shift()!;
    if (typeof n === 'string') continue;
    if (offers(n)) return n as unknown as T;
    queue.push(...n.children);
  }
  throw new Error(`no accessibility element offers the action "${actionName}"`);
}
