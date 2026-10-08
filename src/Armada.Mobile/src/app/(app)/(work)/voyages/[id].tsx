import { useLocalSearchParams } from 'expo-router';
import { ListDetailRoute } from '@/navigation/listDetail';
import { MissionsHubScreen } from '@/screens/operations/MissionsHubScreen';
import { VoyageDetail } from '@/screens/operations/VoyageDetail';

/**
 * /voyages/:id: one item. Opened on a window wide enough for list and detail, it shows the Missions hub's Voyages
 * list with this item selected beside it (deep links and notifications land with the list on the left); narrower
 * windows show the item alone.
 */
export default function Screen() {
  const params = useLocalSearchParams<{ id?: string }>();
  const key = String(params.id ?? '');
  return (
    <ListDetailRoute
      path={`/voyages/${key}`}
      id={key}
      tab="voyages"
      list={<MissionsHubScreen />}
      detail={<VoyageDetail id={key} />}
    />
  );
}
