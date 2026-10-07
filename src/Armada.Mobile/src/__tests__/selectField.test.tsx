import { fireEvent, screen } from '@testing-library/react-native';
import { Keyboard } from 'react-native';
import { SelectField } from '../components/ui';
import { renderWithProviders } from '../test/render';

describe('SelectField', () => {
  it('dismisses the keyboard when it opens, so the sheet is not covered', async () => {
    const dismiss = jest.spyOn(Keyboard, 'dismiss');
    const onChange = jest.fn();
    await renderWithProviders(
      <SelectField label="Vessel" value="" onChange={onChange} placeholder="Select a vessel..." closeLabel="Close"
        options={[{ value: 'vsl_1', label: 'api' }]} testID="pick" />,
    );
    await fireEvent.press(screen.getByTestId('pick'));
    expect(dismiss).toHaveBeenCalled();
    await fireEvent.press(screen.getByTestId('pick-option-vsl_1'));
    expect(onChange).toHaveBeenCalledWith('vsl_1');
    dismiss.mockRestore();
  });
});
