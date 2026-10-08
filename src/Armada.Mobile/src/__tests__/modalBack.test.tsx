import { act, fireEvent, renderHook, screen } from '@testing-library/react-native';
import { Keyboard, Platform, Text } from 'react-native';
import { BottomSheet } from '../components/ui';
import { KEYBOARD_BACK_WINDOW_MS, useModalBack } from '../components/ui/useModalBack';
import { renderWithProviders } from '../test/render';

/** Keyboard events the hook listens to, fired by the test. */
const keyboardListeners = new Map<string, () => void>();
let clock = 10_000;
const now = () => clock;

beforeEach(() => {
  keyboardListeners.clear();
  clock = 10_000;
  jest.replaceProperty(Platform, 'OS', 'android');
  jest.spyOn(Keyboard, 'isVisible').mockReturnValue(false);
  jest.spyOn(Keyboard, 'dismiss').mockImplementation(() => undefined);
  jest.spyOn(Keyboard, 'addListener').mockImplementation(((event: string, handler: () => void) => {
    keyboardListeners.set(event, handler);
    return { remove: () => keyboardListeners.delete(event) };
  }) as unknown as typeof Keyboard.addListener);
});

afterEach(() => {
  jest.restoreAllMocks();
});

describe('useModalBack (Android back in a sheet or dialog with text fields)', () => {
  it('closes the keyboard first and the modal on the next back', async () => {
    const onClose = jest.fn();
    const { result } = await renderHook(() => useModalBack(onClose, now));
    await act(async () => { keyboardListeners.get('keyboardDidShow')?.(); });
    // One back with the keyboard up: only the keyboard goes.
    await act(async () => { result.current(); });
    expect(Keyboard.dismiss).toHaveBeenCalledTimes(1);
    expect(onClose).not.toHaveBeenCalled();
    await act(async () => { keyboardListeners.get('keyboardDidHide')?.(); });
    // The same press can reach the modal just after the keyboard's hide event: still the keyboard's press.
    clock += KEYBOARD_BACK_WINDOW_MS - 1;
    await act(async () => { result.current(); });
    expect(onClose).not.toHaveBeenCalled();
    // The next back closes the modal.
    clock += 1;
    await act(async () => { result.current(); });
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it('closes at once when no keyboard was up, and on iOS', async () => {
    const onClose = jest.fn();
    const { result } = await renderHook(() => useModalBack(onClose, now));
    await act(async () => { result.current(); });
    expect(onClose).toHaveBeenCalledTimes(1);
    jest.replaceProperty(Platform, 'OS', 'ios');
    await act(async () => { keyboardListeners.get('keyboardDidShow')?.(); });
    await act(async () => { result.current(); });
    expect(onClose).toHaveBeenCalledTimes(2);
  });

  it('BottomSheet uses it for the Modal back request', async () => {
    const onClose = jest.fn();
    await renderWithProviders(
      <BottomSheet open title="Edit" onClose={onClose} closeLabel="Close" testID="sheet"><Text>Body</Text></BottomSheet>,
    );
    await act(async () => { keyboardListeners.get('keyboardDidShow')?.(); });
    // The Modal host carries onRequestClose (what Android's back calls).
    const [modal] = screen.container.queryAll((node) => typeof node.props.onRequestClose === 'function');
    await act(async () => { (modal.props.onRequestClose as () => void)(); });
    expect(onClose).not.toHaveBeenCalled();
    expect(Keyboard.dismiss).toHaveBeenCalled();
    // The close button is not a back press: it closes even with the keyboard up.
    await fireEvent.press(screen.getByTestId('sheet-close'));
    expect(onClose).toHaveBeenCalledTimes(1);
  });
});
