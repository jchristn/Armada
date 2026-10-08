import { act, fireEvent, screen } from '@testing-library/react-native';
import { useState } from 'react';
import { Dimensions, StyleSheet, Text, TextInput, View } from 'react-native';
import { BottomSheet, ListRow, PaneWidthContext, SplitView, StatusBadge } from '../components/ui';
import { InitialSelectionContext, ListDetailRoute, useListSelection } from '../navigation/listDetail';
import { mockRouter } from '../test/routerMock';
import {
  COMPACT_MAX_WIDTH,
  EXPANDED_MIN_WIDTH,
  RAIL_WIDTH,
  SIDEBAR_WIDTH,
  SPLIT_MIN_CONTENT_WIDTH,
  formSheetFits,
  layoutFor,
  masterPaneWidth,
  navModeFor,
} from '../navigation/useLayout';
import { renderWithProviders } from '../test/render';

jest.mock('expo-router', () => require('../test/routerMock').routerMockFactory());

// The window drives every layout decision. Tests change it the way the platform does (rotation, Split View, Stage
// Manager): Dimensions emits a change and every component reading the window re-renders on its own.
async function setWindow(width: number, height: number) {
  await act(async () => {
    Dimensions.set({ window: { width, height, scale: 2, fontScale: 1 }, screen: { width, height, scale: 2, fontScale: 1 } });
  });
}

beforeEach(async () => {
  jest.clearAllMocks();
  await setWindow(390, 844);
});

afterAll(async () => { await setWindow(390, 844); });

describe('layout decisions by window size (not device type)', () => {
  // [description, width, height, navigation, list and detail side by side]
  it.each([
    ['iPhone portrait', 402, 874, 'tabs', false],
    ['iPhone 18 Pro Max portrait', 440, 956, 'tabs', false],
    ['iPhone 18 Pro Max landscape', 956, 440, 'rail', true],
    ['iPad mini portrait', 744, 1133, 'rail', true],
    ['iPad mini landscape', 1133, 744, 'sidebar', true],
    ['iPad Pro 11 portrait', 834, 1210, 'rail', true],
    ['iPad Pro 11 landscape', 1210, 834, 'sidebar', true],
    ['iPad Pro 13 portrait', 1032, 1376, 'sidebar', true],
    ['iPad Slide Over', 375, 1000, 'tabs', false],
    ['iPad Split View, half of a 13-inch landscape', 683, 1032, 'rail', false],
    ['iPad Split View, two thirds of a 13-inch landscape', 910, 1032, 'rail', true],
    ['Stage Manager small window', 700, 600, 'rail', false],
    ['Android tablet landscape (Armada_Tablet)', 1280, 800, 'sidebar', true],
    ['Android tablet portrait', 800, 1280, 'rail', true],
    ['foldable, folded', 412, 915, 'tabs', false],
    ['foldable, unfolded', 841, 701, 'rail', true],
  ])('%s (%i x %i): %s, split %s', (_name, width, height, nav, split) => {
    const layout = layoutFor(width, height);
    expect(layout.navMode).toBe(nav);
    expect(layout.isTablet).toBe(nav !== 'tabs');
    expect(layout.split).toBe(split);
    expect(layout.contentWidth).toBe(width - layout.navWidth);
  });

  it('switches at the documented widths', () => {
    expect(navModeFor(COMPACT_MAX_WIDTH - 1)).toBe('tabs');
    expect(navModeFor(COMPACT_MAX_WIDTH)).toBe('rail');
    expect(navModeFor(EXPANDED_MIN_WIDTH - 1)).toBe('rail');
    expect(navModeFor(EXPANDED_MIN_WIDTH)).toBe('sidebar');
    expect(layoutFor(SPLIT_MIN_CONTENT_WIDTH + RAIL_WIDTH, 900).split).toBe(true);
    expect(layoutFor(SPLIT_MIN_CONTENT_WIDTH + RAIL_WIDTH - 1, 900).split).toBe(false);
  });

  it('follows the sidebar preference, except on phone-narrow windows', () => {
    // Expanding the rail on an iPad mini leaves too little room for both panes.
    const expanded = layoutFor(744, 1133, 'expanded');
    expect(expanded.navMode).toBe('sidebar');
    expect(expanded.contentWidth).toBe(744 - SIDEBAR_WIDTH);
    expect(expanded.split).toBe(false);
    expect(layoutFor(1210, 834, 'collapsed').navMode).toBe('rail');
    expect(layoutFor(390, 844, 'expanded').navMode).toBe('tabs');
  });

  it('sizes the list pane to the pane, keeping the detail readable', () => {
    expect(masterPaneWidth(668)).toBe(280);
    expect(masterPaneWidth(758)).toBe(303);
    expect(masterPaneWidth(930)).toBe(360);
    expect(masterPaneWidth(930, 420)).toBe(372);
    expect(masterPaneWidth(640)).toBe(280);
  });

  it('presents sheets as form sheets only with room around them', () => {
    expect(formSheetFits(834, 1210)).toBe(true);
    expect(formSheetFits(1280, 800)).toBe(true);
    expect(formSheetFits(956, 440)).toBe(false);
    expect(formSheetFits(440, 956)).toBe(false);
  });
});

/** A list with a draft in its search box, so tests can see whether the list kept its state. */
function DraftList({ onSelect }: { onSelect: (id: string) => void }) {
  const [draft, setDraft] = useState('');
  return (
    <View>
      <TextInput testID="draft" value={draft} onChangeText={setDraft} />
      <Text testID="row-a" onPress={() => onSelect('a')}>a</Text>
    </View>
  );
}

function ListDetailHarness() {
  const selection = useListSelection((id) => `/items/${id}`);
  return (
    <SplitView
      master={<DraftList onSelect={selection.select} />}
      detail={selection.selectedId ? <Text testID="detail">{`item ${selection.selectedId}`}</Text> : null}
      onBack={selection.clear}
      backLabel="Back"
      testID="harness"
    />
  );
}

describe('SplitView and list selection by pane width', () => {
  it('shows list and detail side by side when the content pane fits both (iPad mini portrait)', async () => {
    await setWindow(744, 1133);
    await renderWithProviders(<ListDetailHarness />);
    expect(screen.getByTestId('split-view')).toBeTruthy();
    await fireEvent.press(screen.getByTestId('row-a'));
    expect(screen.getByTestId('detail')).toHaveTextContent('item a');
    expect(screen.getByTestId('row-a')).toBeTruthy();
    expect(mockRouter.push).not.toHaveBeenCalled();
  });

  it('pushes the item route when the pane is too narrow for both', async () => {
    await setWindow(683, 1032);
    await renderWithProviders(<ListDetailHarness />);
    expect(screen.queryByTestId('split-view')).toBeNull();
    await fireEvent.press(screen.getByTestId('row-a'));
    expect(mockRouter.push).toHaveBeenCalledWith('/items/a');
    expect(screen.queryByTestId('detail')).toBeNull();
  });

  it('keeps the selection, the list, and its draft when the window narrows and widens again', async () => {
    await setWindow(1210, 834);
    await renderWithProviders(<ListDetailHarness />);
    await fireEvent.changeText(screen.getByTestId('draft'), 'half-typed search');
    await fireEvent.press(screen.getByTestId('row-a'));

    // Split View narrows the window: the detail stays on screen, alone, with a way back; the list is only hidden.
    await setWindow(683, 1032);
    expect(screen.queryByTestId('split-view')).toBeNull();
    expect(screen.getByTestId('detail')).toHaveTextContent('item a');
    expect(screen.getByTestId('split-view-back')).toBeTruthy();
    expect(StyleSheet.flatten(screen.getByTestId('harness-master', { includeHiddenElements: true }).props.style)).toMatchObject({ display: 'none' });

    // Back to full width: both panes, the same selection, and the list's draft.
    await setWindow(1210, 834);
    expect(screen.getByTestId('split-view')).toBeTruthy();
    expect(screen.getByTestId('detail')).toHaveTextContent('item a');
    expect(screen.getByTestId('draft').props.value).toBe('half-typed search');
    expect(screen.queryByTestId('split-view-back')).toBeNull();
  });

  it('Back on a detail shown alone returns to the list', async () => {
    await setWindow(1210, 834);
    await renderWithProviders(<ListDetailHarness />);
    await fireEvent.press(screen.getByTestId('row-a'));
    await setWindow(683, 1032);
    await fireEvent.press(screen.getByTestId('split-view-back'));
    expect(screen.queryByTestId('detail')).toBeNull();
    expect(StyleSheet.flatten(screen.getByTestId('harness-master').props.style)).not.toMatchObject({ display: 'none' });
  });

  it('decides by the measured pane, not the window, once laid out (a page sheet narrower than the window)', async () => {
    await setWindow(1210, 834);
    await renderWithProviders(<SplitView master={<Text>list</Text>} detail={<Text testID="detail">detail</Text>} testID="pane" />);
    expect(screen.getByTestId('split-view')).toBeTruthy();
    await act(async () => {
      fireEvent(screen.getByTestId('split-view'), 'layout', { nativeEvent: { layout: { x: 0, y: 0, width: 540, height: 700 } } });
    });
    expect(screen.queryByTestId('split-view')).toBeNull();
    expect(screen.getByTestId('pane-single')).toBeTruthy();
  });
});

describe('detail routes open beside their list on wide windows', () => {
  function Items() {
    const selection = useListSelection((id) => `/items/${id}`);
    return <Text testID="list-selection">{selection.selectedId ?? 'none'}</Text>;
  }

  it('a deep link renders the list with its item selected when both panes fit', async () => {
    await setWindow(834, 1210);
    await renderWithProviders(<ListDetailRoute path="/items/a" id="a" list={<Items />} detail={<Text testID="alone">alone</Text>} />);
    expect(screen.getByTestId('list-selection')).toHaveTextContent('a');
    expect(screen.queryByTestId('alone')).toBeNull();
  });

  it('a phone gets the item alone, as before', async () => {
    await setWindow(402, 874);
    await renderWithProviders(<ListDetailRoute path="/items/a" id="a" list={<Items />} detail={<Text testID="alone">alone</Text>} />);
    expect(screen.getByTestId('alone')).toBeTruthy();
    expect(screen.queryByTestId('list-selection')).toBeNull();
  });

  it('a create route ("new", passed as no id) stays on its own page', async () => {
    await setWindow(1210, 834);
    await renderWithProviders(<ListDetailRoute path="/items/new" id="" list={<Items />} detail={<Text testID="alone">form</Text>} />);
    expect(screen.getByTestId('alone')).toBeTruthy();
  });

  it('the decision is kept when the window changes later (no screen swap, no lost state)', async () => {
    await setWindow(1210, 834);
    const ui = <ListDetailRoute path="/items/a" id="a" list={<Items />} detail={<Text testID="alone">alone</Text>} />;
    await renderWithProviders(ui);
    await setWindow(402, 874);
    expect(screen.getByTestId('list-selection')).toHaveTextContent('a');
    expect(screen.queryByTestId('alone')).toBeNull();
  });

  it('only the list whose route matches takes the selection', async () => {
    await setWindow(1210, 834);
    function Other() {
      const selection = useListSelection((id) => `/other/${id}`);
      return <Text testID="other-selection">{selection.selectedId ?? 'none'}</Text>;
    }
    await renderWithProviders(
      <InitialSelectionContext.Provider value={{ path: '/items/a', id: 'a' }}>
        <Items />
        <Other />
      </InitialSelectionContext.Provider>,
    );
    expect(screen.getByTestId('list-selection')).toHaveTextContent('a');
    expect(screen.getByTestId('other-selection')).toHaveTextContent('none');
  });
});

describe('rows adapt to their pane', () => {
  const row = <ListRow title="A vessel with a long name" subtitle="https://github.com/example/repository" accessory={<StatusBadge label="in sync" tone="success" />} onPress={() => undefined} testID="row" />;

  it('stack the badge under the text in a narrow list pane beside a detail', async () => {
    await setWindow(834, 1210);
    await renderWithProviders(<PaneWidthContext.Provider value={303}>{row}</PaneWidthContext.Provider>);
    expect(screen.getByTestId('row-accessory-stacked')).toBeTruthy();
  });

  it('keep the badge beside the text in a wide pane, even on a phone-sized window', async () => {
    await setWindow(1210, 834);
    await renderWithProviders(<PaneWidthContext.Provider value={420}>{row}</PaneWidthContext.Provider>);
    expect(screen.queryByTestId('row-accessory-stacked')).toBeNull();
    await setWindow(402, 874);
    expect(screen.queryByTestId('row-accessory-stacked')).toBeNull();
  });

  it('get their pane width from SplitView', async () => {
    await setWindow(834, 1210);
    await renderWithProviders(<SplitView master={row} detail={<Text>detail</Text>} />);
    // 834 - 76 (rail) = 758 dp of content; the list pane is 40% of it (303 dp): compact rows.
    expect(screen.getByTestId('row-accessory-stacked')).toBeTruthy();
  });
});

describe('sheets', () => {
  const sheet = <BottomSheet open title="Edit vessel" onClose={() => undefined} closeLabel="Close" testID="sheet"><Text>fields</Text></BottomSheet>;

  it('is a centered form sheet on a tablet', async () => {
    await setWindow(834, 1210);
    await renderWithProviders(sheet);
    const style = StyleSheet.flatten(screen.getByTestId('sheet').props.style);
    expect(style.maxWidth).toBe(600);
    expect(style.borderRadius).toBeGreaterThan(0);
    expect(style.borderBottomLeftRadius).toBeUndefined();
  });

  it('stays a bottom sheet on a phone, in portrait and in landscape', async () => {
    await setWindow(440, 956);
    await renderWithProviders(sheet);
    expect(StyleSheet.flatten(screen.getByTestId('sheet').props.style).borderTopLeftRadius).toBeGreaterThan(0);
    expect(StyleSheet.flatten(screen.getByTestId('sheet').props.style).borderRadius).toBeUndefined();
    await setWindow(956, 440);
    expect(StyleSheet.flatten(screen.getByTestId('sheet').props.style).borderRadius).toBeUndefined();
  });
});
