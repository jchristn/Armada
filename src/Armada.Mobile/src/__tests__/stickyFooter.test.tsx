import { act, screen, within } from '@testing-library/react-native';
import { DeviceEventEmitter, StyleSheet, Text } from 'react-native';
import { BottomSheet, Button, FormActions, Screen } from '../components/ui';
import { renderWithProviders } from '../test/render';

/** True when the element sits inside a (host) ScrollView, i.e. it moves with the scrolled content. */
function insideScrollView(element: { parent: unknown; type: unknown }): boolean {
  let node = element.parent as { parent: unknown; type: unknown } | null;
  while (node) {
    if (node.type === 'RCTScrollView') return true;
    node = node.parent as { parent: unknown; type: unknown } | null;
  }
  return false;
}

const longForm = Array.from({ length: 40 }, (_v, i) => <Text key={i}>{`Field ${i + 1}`}</Text>);

describe('form footers (primary action reachable without scrolling to the end)', () => {
  it('Screen renders its footer below the scrolling content, not inside it', async () => {
    await renderWithProviders(
      <Screen testID="form" footer={<FormActions><Button label="Save" onPress={jest.fn()} testID="form-save" /></FormActions>}>
        {longForm}
      </Screen>,
    );
    const save = within(screen.getByTestId('form-footer')).getByTestId('form-save');
    expect(insideScrollView(save)).toBe(false);
    expect(insideScrollView(screen.getByText('Field 40'))).toBe(true);
  });

  it('Screen lifts its footer above the keyboard by the overlap measured in the window, under a header', async () => {
    const nativeMethods = jest.requireActual('@react-native/jest-preset/jest/MockNativeMethods').default as { measureInWindow: jest.Mock };
    // Create Voyage on an iPhone 17 (874 dp): the screen starts below a 100 dp header and ends above an 83 dp tab bar.
    nativeMethods.measureInWindow.mockImplementation((callback: (x: number, y: number, w: number, h: number) => void) => callback(0, 100, 402, 691));
    try {
      await renderWithProviders(
        <Screen testID="form" footer={<FormActions><Button label="Save" onPress={jest.fn()} testID="form-save" /></FormActions>}>
          {longForm}
        </Screen>,
      );
      const lift = () => StyleSheet.flatten(screen.getByTestId('form-keyboard-avoider').props.style).paddingBottom;
      expect(lift()).toBe(0);
      // The keyboard's top at 546 covers the screen's last 245 dp (100 + 691 - 546), header included in the math.
      await act(async () => {
        DeviceEventEmitter.emit('keyboardWillShow', { endCoordinates: { screenX: 0, screenY: 546, width: 402, height: 328 }, duration: 0, easing: 'keyboard' });
      });
      expect(lift()).toBe(245);
      await act(async () => {
        DeviceEventEmitter.emit('keyboardWillHide', { endCoordinates: { screenX: 0, screenY: 874, width: 402, height: 0 }, duration: 0, easing: 'keyboard' });
      });
      expect(lift()).toBe(0);
    } finally {
      nativeMethods.measureInWindow.mockReset();
    }
  });

  it('BottomSheet renders its footer below the scrolling body, not inside it', async () => {
    await renderWithProviders(
      <BottomSheet open title="Edit" onClose={jest.fn()} closeLabel="Close" testID="sheet"
        footer={<FormActions><Button label="Save" onPress={jest.fn()} testID="sheet-save" /></FormActions>}>
        {longForm}
      </BottomSheet>,
    );
    const save = within(screen.getByTestId('sheet-footer')).getByTestId('sheet-save');
    expect(insideScrollView(save)).toBe(false);
    expect(insideScrollView(screen.getByText('Field 40'))).toBe(true);
  });

  it('a Screen without a footer renders none', async () => {
    await renderWithProviders(<Screen testID="plain">{longForm}</Screen>);
    expect(screen.queryByTestId('plain-footer')).toBeNull();
  });
});
