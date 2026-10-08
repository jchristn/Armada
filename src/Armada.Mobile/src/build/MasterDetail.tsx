import type { ReactNode } from 'react';
import { StyleSheet, View } from 'react-native';
import { EmptyState, SplitView } from '../components/ui';
import type { IconName } from '../components/ui';
import { useLocale } from '../i18n/LocaleContext';
import { useListSelection } from '../navigation/listDetail';

export interface Selection {
  /** The item shown in the detail pane (kept when the window narrows; then shown alone with a Back button). */
  selectedId: string | null;
  /** Open an item: in place when list and detail fit side by side, otherwise its route is pushed. */
  open: (id: string) => void;
  clear: () => void;
  /** List and detail are side by side. */
  isTablet: boolean;
}

/** List-detail selection by content-pane width (see useListSelection). */
export function useSelection(hrefFor: (id: string) => string): Selection {
  const { selectedId, select, clear, split } = useListSelection(hrefFor);
  return { selectedId, open: select, clear, isTablet: split };
}

export interface MasterDetailProps {
  selection: Selection;
  master: ReactNode;
  /** The detail of the selected item. */
  renderDetail: (id: string) => ReactNode;
  /** What the empty detail pane says when list and detail are side by side. */
  emptyTitle: string;
  emptyIcon?: IconName;
}

/**
 * A hub list with its detail beside it when the content pane fits both, on its own route otherwise; a selection
 * made side by side stays on screen (alone, with Back) when the window narrows.
 */
export function MasterDetail({ selection, master, renderDetail, emptyTitle, emptyIcon = 'albums-outline' }: MasterDetailProps) {
  const { t } = useLocale();
  const detail = selection.selectedId
    ? <View style={styles.fill} key={selection.selectedId}>{renderDetail(selection.selectedId)}</View>
    : selection.isTablet ? <EmptyState icon={emptyIcon} title={emptyTitle} /> : null;
  return <SplitView master={master} detail={detail} masterWidth={420} onBack={selection.clear} backLabel={t('Back')} />;
}

const styles = StyleSheet.create({ fill: { flex: 1 } });
