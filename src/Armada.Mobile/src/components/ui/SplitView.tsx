import { useEffect, useRef, useState, type ReactNode } from 'react';
import { StyleSheet, View } from 'react-native';
import { focusElement } from '../../lib/accessibility';
import { masterPaneWidth, splitFits, useLayout } from '../../navigation/useLayout';
import { useTheme } from '../../theme/ThemeContext';
import { spacing } from '../../theme/typography';
import { Button } from './Button';
import { PaneWidthContext } from './paneWidth';
import { usePaneBack } from './usePaneBack';

export interface SplitViewProps {
  /** The list. */
  master: ReactNode;
  /** The selected item (or an empty-state hint). When the pane is too narrow for both, it is shown instead of the list. */
  detail: ReactNode | null;
  /** Largest width of the list pane; it takes 40% of the pane up to this (at least 280 dp). */
  masterWidth?: number;
  /** Narrowest content pane that shows both panes (a screen with a wide list can ask for more room). */
  minWidth?: number;
  /**
   * Leaves a detail shown alone in a narrow pane (the window narrowed while an item was selected): a Back button
   * above it and Android back call this, usually clearing the selection. Without it the detail has no way back
   * (screens whose detail is the whole page, such as Ask).
   */
  onBack?: () => void;
  /** Label of that Back button. */
  backLabel?: string;
  testID?: string;
}

/**
 * List-detail layout, decided by the width of the content pane (the window minus the sidebar or rail), not the
 * device: wide panes show list and detail side by side; narrow panes show the detail when one is given and the list
 * otherwise, so the same screen code serves phones, tablets, Split View, Stage Manager, and foldables.
 *
 * Both panes keep their place in the tree when the window changes size, so rotating or resizing keeps the selected
 * item, scroll positions, drafts, and open sheets: the list stays mounted (hidden) while a detail is shown alone.
 */
export function SplitView({ master, detail, masterWidth = 360, minWidth, onBack, backLabel, testID }: SplitViewProps) {
  const { contentWidth } = useLayout();
  const { colors } = useTheme();
  // The pane's measured width once laid out (a modal or a nested pane can be narrower than the content area);
  // until then the content area's width, which is what a top-level screen gets.
  const [measured, setMeasured] = useState<number | null>(null);
  const paneWidth = measured ?? contentWidth;
  const split = splitFits(paneWidth, minWidth);
  const listWidth = split ? masterPaneWidth(paneWidth, masterWidth) : paneWidth;
  const detailWidth = split ? paneWidth - listWidth : paneWidth;
  const detailAlone = !split && detail !== null && detail !== undefined;
  // The list mounts once it has been visible; after that it stays mounted while hidden (scroll and search kept).
  const [masterMounted, setMasterMounted] = useState(!detailAlone);
  if (!detailAlone && !masterMounted) setMasterMounted(true);
  usePaneBack(detailAlone && !!onBack, onBack);
  // When a selection replaces the list in a narrow pane, the row a screen reader was on disappears: focus moves to
  // the Back button at the top of the detail, so VoiceOver and TalkBack users land where the new content starts.
  const backRef = useRef<View | null>(null);
  const wasAlone = useRef(detailAlone);
  useEffect(() => {
    if (detailAlone && !wasAlone.current && onBack) focusElement(backRef);
    wasAlone.current = detailAlone;
  }, [detailAlone, onBack]);

  return (
    <View
      style={styles.row}
      testID={split ? 'split-view' : testID ? `${testID}-single` : undefined}
      onLayout={(event) => {
        const next = Math.round(event.nativeEvent.layout.width);
        if (next > 0 && next !== measured) setMeasured(next);
      }}
    >
      {masterMounted ? (
        <View
          testID={testID ? `${testID}-master` : undefined}
          style={split
            ? [styles.master, { width: listWidth, borderRightColor: colors.border }]
            : detailAlone ? styles.hidden : styles.fill}
        >
          <PaneWidthContext.Provider value={listWidth}>{master}</PaneWidthContext.Provider>
        </View>
      ) : null}
      {split || detailAlone ? (
        <View style={styles.fill} testID={testID ? `${testID}-detail` : undefined}>
          {detailAlone && onBack ? (
            <View style={[styles.back, { borderBottomColor: colors.border }]}>
              <Button ref={backRef} label={backLabel ?? 'Back'} variant="ghost" icon="chevron-back" onPress={onBack} testID="split-view-back" />
            </View>
          ) : null}
          <PaneWidthContext.Provider value={detailWidth}>{detail}</PaneWidthContext.Provider>
        </View>
      ) : null}
    </View>
  );
}

const styles = StyleSheet.create({
  fill: { flex: 1 },
  hidden: { display: 'none' },
  row: { flex: 1, flexDirection: 'row' },
  master: { borderRightWidth: StyleSheet.hairlineWidth },
  back: { alignItems: 'flex-start', paddingHorizontal: spacing.sm, borderBottomWidth: StyleSheet.hairlineWidth },
});
