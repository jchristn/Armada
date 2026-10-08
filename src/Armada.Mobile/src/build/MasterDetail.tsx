import { useRouter, type Href } from 'expo-router';
import { useCallback, useState, type ReactNode } from 'react';
import { StyleSheet, View } from 'react-native';
import { EmptyState, SplitView } from '../components/ui';
import type { IconName } from '../components/ui';
import { useLayout } from '../navigation/useLayout';

export interface Selection {
  /** The item shown in the detail pane (tablets only; phones navigate instead). */
  selectedId: string | null;
  /** Open an item: tablets select it in place, phones push its route. */
  open: (id: string) => void;
  clear: () => void;
  isTablet: boolean;
}

/** List-detail selection: in place on tablets (split view), a pushed route on phones. */
export function useSelection(hrefFor: (id: string) => string): Selection {
  const { isTablet } = useLayout();
  const router = useRouter();
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const open = useCallback((id: string) => {
    if (isTablet) setSelectedId(id);
    else router.push(hrefFor(id) as Href);
  }, [isTablet, router, hrefFor]);
  const clear = useCallback(() => setSelectedId(null), []);
  return { selectedId: isTablet ? selectedId : null, open, clear, isTablet };
}

export interface MasterDetailProps {
  selection: Selection;
  master: ReactNode;
  /** The detail of the selected item (rendered only on tablets, when one is selected). */
  renderDetail: (id: string) => ReactNode;
  /** What the empty detail pane says on tablets. */
  emptyTitle: string;
  emptyIcon?: IconName;
}

/** A hub list with its detail beside it on tablets (iPad split view) and on its own route on phones. */
export function MasterDetail({ selection, master, renderDetail, emptyTitle, emptyIcon = 'albums-outline' }: MasterDetailProps) {
  if (!selection.isTablet) return <View style={styles.fill}>{master}</View>;
  const detail = selection.selectedId
    ? <View style={styles.fill} key={selection.selectedId}>{renderDetail(selection.selectedId)}</View>
    : <EmptyState icon={emptyIcon} title={emptyTitle} />;
  return <SplitView master={master} detail={detail} masterWidth={420} />;
}

const styles = StyleSheet.create({ fill: { flex: 1 } });
