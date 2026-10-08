import { screen } from '@testing-library/react-native';
import { SearchField, TextField } from '../components/ui';
import { renderWithProviders } from '../test/render';

// Android wraps a long placeholder in a one-line input onto a second line that the box clips (seen on the W4
// resource lists: "Search by title, environment, ..."). One-line inputs keep their placeholder to one line.
describe('one-line inputs keep a long placeholder on one line', () => {
  it('SearchField', async () => {
    await renderWithProviders(
      <SearchField value="" onChangeText={jest.fn()} placeholder="Search by title, environment, vessel, branch, or ID" clearLabel="Clear" testID="search" />,
    );
    expect(screen.getByTestId('search').props.numberOfLines).toBe(1);
  });

  it('TextField (one line), but not a multiline TextField', async () => {
    await renderWithProviders(
      <>
        <TextField label="Title" value="" onChangeText={jest.fn()} placeholder="Optional override for the voyage title" testID="title" />
        <TextField label="Description" value="" onChangeText={jest.fn()} multiline numberOfLines={6} testID="description" />
        <TextField label="Notes" value="" onChangeText={jest.fn()} multiline testID="notes" />
      </>,
    );
    expect(screen.getByTestId('title').props.numberOfLines).toBe(1);
    expect(screen.getByTestId('description').props.numberOfLines).toBe(6);
    expect(screen.getByTestId('notes').props.numberOfLines).toBeUndefined();
  });
});
