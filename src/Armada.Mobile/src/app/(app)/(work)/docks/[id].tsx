import { useLocalSearchParams } from 'expo-router';
import { ListDetailRoute } from '@/navigation/listDetail';
import { CaptainsHubScreen } from '@/screens/captains/CaptainsHubScreen';
import { DockDetail } from '@/screens/operations/DockDetail';

/**
 * /docks/:id: one item. Opened on a window wide enough for list and detail, it shows the Captains hub's Docks list
 * with this item selected beside it (deep links and notifications land with the list on the left); narrower windows
 * show the item alone.
 */
export default function Screen() {
  const params = useLocalSearchParams<{ id?: string }>();
  const key = String(params.id ?? '');
  return (
    <ListDetailRoute
      path={`/docks/${key}`}
      id={key}
      tab="docks"
      list={<CaptainsHubScreen />}
      detail={<DockDetail id={key} />}
    />
  );
}
