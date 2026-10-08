import { useSplitSelection } from '../../components/app/useSplitSelection';
import { EmptyState, SplitView } from '../../components/ui';
import { useLocale } from '../../i18n/LocaleContext';
import { DockDetail } from '../operations/DockDetail';
import { DocksList } from '../operations/DocksList';

const dockRoute = (id: string) => `/docks/${id}`;

/**
 * The Docks tab of the Captains hub (the dashboard's CaptainsHub embeds its Docks page): W2's Docks list (search,
 * user filter, delete, bulk delete after a long press) with the dock detail beside it on tablets; phones push
 * /docks/:id. A selection made side by side stays on screen (with Back) when the window narrows.
 */
export function DocksTab() {
  const { t } = useLocale();
  const selection = useSplitSelection(dockRoute);
  const list = <DocksList onSelect={selection.select} selectedId={selection.selectedId} />;
  return (
    <SplitView
      master={list}
      detail={selection.selectedId
        ? <DockDetail key={selection.selectedId} id={selection.selectedId} embedded />
        : selection.isTablet ? <EmptyState icon="git-branch-outline" title={t('Select an item to see its details.')} /> : null}
      onBack={selection.clear}
      backLabel={t('Back')}
    />
  );
}
