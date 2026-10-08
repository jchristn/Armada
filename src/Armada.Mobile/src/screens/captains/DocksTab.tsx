import { StyleSheet, View } from 'react-native';
import { useSplitSelection } from '../../components/app/useSplitSelection';
import { EmptyState, SplitView } from '../../components/ui';
import { useLocale } from '../../i18n/LocaleContext';
import { DockDetail } from '../operations/DockDetail';
import { DocksList } from '../operations/DocksList';

const dockRoute = (id: string) => `/docks/${id}`;

/**
 * The Docks tab of the Captains hub (the dashboard's CaptainsHub embeds its Docks page): W2's Docks list (search,
 * user filter, delete, bulk delete after a long press) with the dock detail beside it on tablets; phones push
 * /docks/:id.
 */
export function DocksTab() {
  const { t } = useLocale();
  const selection = useSplitSelection(dockRoute);
  const list = <DocksList onSelect={selection.select} selectedId={selection.selectedId} />;
  if (!selection.isTablet) return <View style={styles.fill}>{list}</View>;
  return (
    <SplitView
      master={list}
      detail={selection.selectedId
        ? <DockDetail key={selection.selectedId} id={selection.selectedId} embedded />
        : <EmptyState icon="git-branch-outline" title={t('Select an item to see its details.')} />}
    />
  );
}

const styles = StyleSheet.create({ fill: { flex: 1 } });
