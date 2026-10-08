import { screen, within } from '@testing-library/react-native';
import { Text } from 'react-native';
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
