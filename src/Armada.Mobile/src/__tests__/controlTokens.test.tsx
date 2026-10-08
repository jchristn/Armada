/**
 * Form controls draw their outline with the `control` token (3:1 against the surface, WCAG 1.4.11) instead of the
 * decorative hairline `border`, and progress tracks use `track` (3:1 against the fill), in every theme.
 */
import AsyncStorage from '@react-native-async-storage/async-storage';
import { render, screen, waitFor } from '@testing-library/react-native';
import { StyleSheet, View } from 'react-native';
import { ProgressBar, SearchField, SelectField, TextField } from '../components/ui';
import { SwitchRow } from '../components/ui/SwitchRow';
import { PREF_KEYS } from '../storage/prefs';
import { palettes, type ThemeName } from '../theme/palette';
import { ThemeProvider, useTheme } from '../theme/ThemeContext';

let themeName: ThemeName | null = null;
function Probe() {
  themeName = useTheme().name;
  return null;
}

function borderOf(testID: string): string | undefined {
  // The outline is on the input's box: the nearest ancestor with a border color.
  let node = screen.getByTestId(testID);
  for (;;) {
    const style = StyleSheet.flatten(node.props.style) as { borderColor?: string } | undefined;
    if (style?.borderColor) return style.borderColor;
    if (!node.parent) return undefined;
    node = node.parent;
  }
}

describe.each(['light', 'dark', 'highContrast'] as ThemeName[])('%s theme', (name) => {
  beforeEach(async () => {
    await AsyncStorage.setItem(PREF_KEYS.theme, JSON.stringify(name));
    themeName = null;
  });

  it('outlines fields with the control token and fills tracks with the track token', async () => {
    const colors = palettes[name];
    await render(
      <ThemeProvider>
        <Probe />
        <View>
          <TextField testID="field" label="Name" value="" onChangeText={() => undefined} />
          <SearchField testID="search" value="" onChangeText={() => undefined} placeholder="Search" clearLabel="Clear" />
          <SelectField testID="select" label="Status" value="" options={[{ value: 'a', label: 'A' }]} onChange={() => undefined} placeholder="All" closeLabel="Close" />
          <SwitchRow testID="switch" label="Enabled" value={false} onChange={() => undefined} />
          <ProgressBar testID="progress" percent={40} label="Progress" />
        </View>
      </ThemeProvider>,
    );
    await waitFor(() => expect(themeName).toBe(name));
    expect(borderOf('field')).toBe(colors.control);
    expect(borderOf('search')).toBe(colors.control);
    expect(borderOf('select')).toBe(colors.control);
    expect(screen.getByTestId('switch').props.tintColor ?? screen.getByTestId('switch').props.trackColor?.false).toBe(colors.control);
    expect((StyleSheet.flatten(screen.getByTestId('progress').props.style) as { backgroundColor?: string }).backgroundColor).toBe(colors.track);
  });
});
