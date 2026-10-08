/**
 * The shared modal frame (ModalOverlay) behind every sheet and dialog: the dim backdrop covers the whole screen,
 * appears without any animation, and is never inside the keyboard-avoiding layer, so the keyboard opening or
 * closing moves only the sheet or dialog content. Sheets, confirm dialogs, and one-time secret dialogs all use it.
 */
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react-native';
import { AccessibilityInfo, StyleSheet, Text, type ViewStyle } from 'react-native';
import type { ReactNode } from 'react';
import { BottomSheet, ConfirmDialog } from '../components/ui';
import { modalEntrance } from '../components/ui/ModalOverlay';
import { SecretOnceDialog } from '../screens/system/TenantAdminCommon';
import { ThemeProvider } from '../theme/ThemeContext';
import { palettes } from '../theme/palette';

jest.mock('expo-router', () => ({ useRouter: () => ({ push: jest.fn() }) }));

const info = AccessibilityInfo as jest.Mocked<typeof AccessibilityInfo>;

function Themed({ children }: { children: ReactNode }) {
  return <ThemeProvider>{children}</ThemeProvider>;
}

interface Node { type: unknown; props: Record<string, unknown>; parent: Node | null; children: (Node | string)[] }

function ancestorsOf(node: Node): Node[] {
  const out: Node[] = [];
  for (let current = node.parent; current; current = current.parent) out.push(current);
  return out;
}

/** The Modal host element (it carries the Modal's props). */
function modalHost(): Node {
  const backdrop = screen.getByTestId('modal-backdrop') as unknown as Node;
  const modal = ancestorsOf(backdrop).find((n) => n.props.animationType !== undefined);
  if (!modal) throw new Error('no Modal above the backdrop');
  return modal;
}

function expectStillFullScreenBackdrop() {
  const backdrop = screen.getByTestId('modal-backdrop') as unknown as Node;
  const style = StyleSheet.flatten(backdrop.props.style as ViewStyle);
  // Covers the screen edge to edge, with no animated opacity or transform (it appears at once).
  expect(style).toMatchObject({ position: 'absolute', top: 0, right: 0, bottom: 0, left: 0 });
  expect(style.opacity).toBeUndefined();
  expect(style.transform).toBeUndefined();
  expect(style.backgroundColor).toBe(palettes.light.overlay);
  // Not inside the keyboard-avoiding layer: the keyboard lifts the content only.
  const ancestors = ancestorsOf(backdrop).map((n) => n.props.testID);
  expect(ancestors).not.toContain('modal-keyboard-layer');
  expect(ancestors).not.toContain('modal-content-layer');
  // The platform Modal does not animate, draws under the status bar and the Android navigation bar.
  expect(modalHost().props).toMatchObject({ animationType: 'none', transparent: true, statusBarTranslucent: true, navigationBarTranslucent: true });
}

beforeEach(() => {
  jest.clearAllMocks();
  info.isReduceMotionEnabled.mockResolvedValue(false);
  info.isScreenReaderEnabled.mockResolvedValue(false);
});

describe('modal backdrop', () => {
  it('a bottom sheet: full-screen, instant, outside the keyboard layer; tapping it closes the sheet', async () => {
    const onClose = jest.fn();
    await render(<Themed><BottomSheet open title="Filters" onClose={onClose} closeLabel="Close" testID="sheet"><Text>body</Text></BottomSheet></Themed>);
    expectStillFullScreenBackdrop();
    // The sheet is inside the keyboard layer (it is what moves).
    const sheetAncestors = ancestorsOf(screen.getByTestId('sheet') as unknown as Node).map((n) => n.props.testID);
    expect(sheetAncestors).toContain('modal-keyboard-layer');
    await fireEvent.press(screen.getByTestId('modal-backdrop'));
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it('a confirm dialog: the same backdrop, which does not close the dialog', async () => {
    const onCancel = jest.fn();
    await render(<Themed><ConfirmDialog open title="Delete?" message="M" confirmLabel="Delete" cancelLabel="Cancel" onConfirm={() => undefined} onCancel={onCancel} typedConfirmation="delete" testID="dlg" /></Themed>);
    expectStillFullScreenBackdrop();
    const fieldAncestors = ancestorsOf(screen.getByTestId('dlg-typed') as unknown as Node).map((n) => n.props.testID);
    expect(fieldAncestors).toContain('modal-keyboard-layer');
    expect(screen.getByTestId('modal-backdrop').props.onPress).toBeUndefined();
    expect(onCancel).not.toHaveBeenCalled();
  });

  it('a one-time secret dialog uses the same frame', async () => {
    await render(<Themed><SecretOnceDialog open title="Secret" message="Copy it now" secretLabel="Token" secret="abc" doneLabel="Done" onClose={() => undefined} testID="secret" /></Themed>);
    expectStillFullScreenBackdrop();
  });

  it('the backdrop stays the same under Reduce Motion (only the content fades instead of sliding)', async () => {
    info.isReduceMotionEnabled.mockResolvedValue(true);
    await render(<Themed><BottomSheet open title="Filters" onClose={() => undefined} closeLabel="Close"><Text>body</Text></BottomSheet></Themed>);
    await act(async () => { await Promise.resolve(); });
    await waitFor(() => expect(StyleSheet.flatten(screen.getByTestId('modal-content-layer').props.style as ViewStyle).transform).toBeUndefined());
    expectStillFullScreenBackdrop();
  });

  it('closed modals render nothing (no backdrop left behind)', async () => {
    const view = await render(<Themed><BottomSheet open title="Filters" onClose={() => undefined} closeLabel="Close"><Text>body</Text></BottomSheet></Themed>);
    expect(screen.getByTestId('modal-backdrop')).toBeTruthy();
    await view.rerender(<Themed><BottomSheet open={false} title="Filters" onClose={() => undefined} closeLabel="Close"><Text>body</Text></BottomSheet></Themed>);
    expect(screen.queryByTestId('modal-backdrop')).toBeNull();
  });
});

describe('modalEntrance', () => {
  it('sheets slide, dialogs fade, everything fades under Reduce Motion', () => {
    expect(modalEntrance('sheet', false)).toBe('slide');
    expect(modalEntrance('sheet', true)).toBe('fade');
    expect(modalEntrance('dialog', false)).toBe('fade');
    expect(modalEntrance('dialog', true)).toBe('fade');
  });
});
