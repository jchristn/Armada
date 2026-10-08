import { screen } from '@testing-library/react-native';
import { Platform } from 'react-native';
import { SearchField } from '../components/ui';
import { renderWithProviders } from '../test/render';

// Android wraps a TextInput placeholder onto as many lines as it needs, and the one-line search box clipped the
// second line in half (the W4 lists: "Search by title, environment, ..."). On Android the placeholder is drawn as one
// ellipsized line over the empty input; iOS keeps the native placeholder.
const LONG = 'Search by title, environment, vessel, branch, or ID';

afterEach(() => jest.restoreAllMocks());

describe('SearchField placeholder', () => {
  it('is one ellipsized line on Android, hidden once there is text', async () => {
    jest.replaceProperty(Platform, 'OS', 'android');
    const view = await renderWithProviders(<SearchField value="" onChangeText={jest.fn()} placeholder={LONG} clearLabel="Clear" testID="search" />);
    expect(screen.getByTestId('search').props.placeholder).toBeUndefined();
    const own = screen.getByTestId('search-placeholder', { includeHiddenElements: true });
    expect(own.props.numberOfLines).toBe(1);
    expect(own.props.ellipsizeMode).toBe('tail');
    expect(own).toHaveTextContent(LONG);
    // The input keeps the placeholder as its accessible name.
    expect(screen.getByTestId('search').props.accessibilityLabel).toBe(LONG);
    await view.rerender(<SearchField value="deploy" onChangeText={jest.fn()} placeholder={LONG} clearLabel="Clear" testID="search" />);
    expect(screen.queryByTestId('search-placeholder', { includeHiddenElements: true })).toBeNull();
  });

  it('is the native placeholder on iOS', async () => {
    await renderWithProviders(<SearchField value="" onChangeText={jest.fn()} placeholder={LONG} clearLabel="Clear" testID="search" />);
    expect(screen.getByTestId('search').props.placeholder).toBe(LONG);
    expect(screen.queryByTestId('search-placeholder', { includeHiddenElements: true })).toBeNull();
  });
});
