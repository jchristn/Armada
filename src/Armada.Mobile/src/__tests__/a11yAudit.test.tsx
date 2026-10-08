import { fireEvent, render, screen } from '@testing-library/react-native';
import { Image, Pressable, Switch, Text, TextInput, View } from 'react-native';
import { ResourceRow } from '../components/resource/ResourceRow';
import { KpiCard, ListRow, StatusBadge, SwipeRow } from '../components/ui';
import { spokenValue } from '../components/ui/ListRow';
import { auditAccessibility, rowActionTarget, type A11yRule } from '../test/a11y';
import { ThemeProvider } from '../theme/ThemeContext';

/** The UI kit only needs the theme. */
function AppProviders({ children }: { children: React.ReactNode }) {
  return <ThemeProvider>{children}</ThemeProvider>;
}

function rules(): A11yRule[] {
  return auditAccessibility(screen.root).map((i) => i.rule);
}

/** Renders a case that is meant to fail the audit, then clears it so the global after-test audit sees a clean tree. */
async function auditOf(ui: React.ReactElement): Promise<A11yRule[]> {
  const result = await render(<AppProviders>{ui}</AppProviders>);
  const found = rules();
  await result.unmount();
  return found;
}

describe('accessibility audit', () => {
  it('flags an icon-only touchable without a role or a name', async () => {
    expect(await auditOf(<Pressable testID="x" onPress={() => undefined}><View /></Pressable>)).toEqual(['touchable-role', 'touchable-name']);
  });

  it('accepts a touchable named by its text or its label', async () => {
    expect(await auditOf(<View>
      <Pressable accessibilityRole="button" onPress={() => undefined}><Text>Save</Text></Pressable>
      <Pressable accessibilityRole="button" accessibilityLabel="Close" onPress={() => undefined}><View /></Pressable>
    </View>)).toEqual([]);
  });

  it('flags unlabelled inputs, switches, and images that screen readers reach', async () => {
    expect(await auditOf(<View>
      <TextInput placeholder="Name" />
      <Switch value onValueChange={() => undefined} />
      <Image source={{ uri: 'https://x/y.png' }} />
    </View>)).toEqual(['input-label', 'switch-label', 'image-label']);
  });

  it('ignores hidden subtrees and decorative images', async () => {
    expect(await auditOf(<View>
      <View importantForAccessibility="no-hide-descendants" accessibilityElementsHidden>
        <Pressable onPress={() => undefined}><View /></Pressable>
      </View>
      <Image source={{ uri: 'https://x/y.png' }} accessible={false} />
    </View>)).toEqual([]);
  });

  it('flags a control inside another accessibility element (unreachable on iOS)', async () => {
    expect(await auditOf(
      <Pressable accessibilityRole="button" accessibilityLabel="Row" onPress={() => undefined}>
        <Pressable accessibilityRole="button" accessibilityLabel="Menu" onPress={() => undefined}><View /></Pressable>
      </Pressable>,
    )).toContain('nested-interactive');
  });

  it('flags screen-reader actions on an element screen readers never focus', async () => {
    expect(await auditOf(
      <View accessibilityActions={[{ name: 'delete', label: 'Delete' }]}>
        <Pressable accessibilityRole="button" accessibilityLabel="Row" onPress={() => undefined}><View /></Pressable>
      </View>,
    )).toEqual(['unreachable-actions']);
  });

  it('flags a row that shows text its label leaves out', async () => {
    expect(await auditOf(
      <Pressable accessibilityRole="button" accessibilityLabel="Fix login" onPress={() => undefined}>
        <Text>Fix login</Text>
        <StatusBadge label="Failed" tone="failed" />
      </Pressable>,
    )).toEqual(['summary-incomplete']);
  });
});

describe('list rows read what they show', () => {
  it('a status accessory is read as the row value', async () => {
    await render(<AppProviders>
      <ListRow testID="r" title="Fix login" subtitle="api" accessory={<StatusBadge label="Failed" tone="failed" />} accessibilityValue={['Failed', null, '']} onPress={() => undefined} />
    </AppProviders>);
    expect(screen.getByTestId('r').props.accessibilityValue).toEqual({ text: 'Failed' });
    expect(rules()).toEqual([]);
  });

  it('spoken values drop empty pieces', () => {
    expect(spokenValue(['Failed', null, undefined, false, '', ' ', 3])).toBe('Failed, 3');
    expect(spokenValue(undefined)).toBe('');
  });

  it('a menu button sits beside the row and is also the row\'s screen-reader action', async () => {
    const menu = jest.fn();
    const open = jest.fn();
    await render(<AppProviders>
      <ListRow testID="r" title="Fix login" onPress={open} menu={{ label: 'Actions', onPress: menu, testID: 'r-menu' }} />
    </AppProviders>);
    expect(rules()).toEqual([]);
    const row = screen.getByTestId('r');
    expect(row.props.accessibilityActions).toEqual([{ name: 'menu', label: 'Actions' }]);
    await fireEvent(row, 'accessibilityAction', { nativeEvent: { actionName: 'menu' } });
    expect(menu).toHaveBeenCalledTimes(1);
    await fireEvent.press(screen.getByTestId('r-menu'));
    expect(menu).toHaveBeenCalledTimes(2);
    expect(open).not.toHaveBeenCalled();
  });

  it('a long press is offered as an action when it has a name', async () => {
    const longPress = jest.fn();
    await render(<AppProviders>
      <ListRow testID="r" title="Fix login" onPress={() => undefined} onLongPress={longPress} longPressLabel="Select" />
    </AppProviders>);
    await fireEvent(screen.getByTestId('r'), 'accessibilityAction', { nativeEvent: { actionName: 'longpress' } });
    expect(longPress).toHaveBeenCalledTimes(1);
  });

  it('swipe actions are offered on the row screen readers focus, next to its own actions', async () => {
    const remove = jest.fn();
    const menu = jest.fn();
    await render(<AppProviders>
      <SwipeRow testID="s" actions={[{ key: 'delete', label: 'Delete', icon: 'trash-outline', onPress: remove }]}>
        <ListRow testID="r" title="Fix login" onPress={() => undefined} menu={{ label: 'Actions', onPress: menu }} />
      </SwipeRow>
    </AppProviders>);
    expect(rules()).toEqual([]);
    const row = screen.getByTestId('r');
    expect(row.props.accessibilityActions).toEqual([{ name: 'menu', label: 'Actions' }, { name: 'delete', label: 'Delete' }]);
    expect(screen.getByTestId('s').props.accessibilityActions).toBeUndefined();
    await fireEvent(rowActionTarget(screen.getByTestId('s'), 'delete'), 'accessibilityAction', { nativeEvent: { actionName: 'delete' } });
    expect(remove).toHaveBeenCalledTimes(1);
    await fireEvent(row, 'accessibilityAction', { nativeEvent: { actionName: 'menu' } });
    expect(menu).toHaveBeenCalledTimes(1);
  });

  it('a resource row reads its badge and its meta text', async () => {
    await render(<AppProviders>
      <ResourceRow testID="r" title="Release 2.3" badge={{ label: 'Passed', tone: 'success' }} meta="6 days ago" onPress={() => undefined} />
    </AppProviders>);
    expect(screen.getByTestId('r').props.accessibilityValue).toEqual({ text: 'Passed, 6 days ago' });
    expect(rules()).toEqual([]);
  });

  it('a KPI card reads its detail chips', async () => {
    await render(<AppProviders>
      <KpiCard testID="k" label="Captains" value={3} onPress={() => undefined} accessibilityValue="1 idle, 2 working">
        <StatusBadge label="1 idle" tone="success" />
        <StatusBadge label="2 working" tone="running" />
      </KpiCard>
    </AppProviders>);
    expect(screen.getByTestId('k').props.accessibilityValue).toEqual({ text: '1 idle, 2 working' });
    expect(rules()).toEqual([]);
  });
});
