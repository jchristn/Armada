import { useLocalSearchParams } from 'expo-router';
import { ListDetailRoute } from '@/navigation/listDetail';
import { FleetDetailView } from '@/screens/fleets/FleetDetailView';
import { VesselsHubScreen } from '@/screens/vessels/VesselsHubScreen';

/**
 * /fleets/:id: one item. Opened on a window wide enough for list and detail, it shows the Vessels hub's Fleets list
 * with this item selected beside it (deep links and notifications land with the list on the left); narrower windows
 * show the item alone.
 */
export default function Screen() {
  const params = useLocalSearchParams<{ id?: string }>();
  const key = String(params.id ?? '');
  return (
    <ListDetailRoute
      path={`/fleets/${key}`}
      id={key}
      tab="fleets"
      list={<VesselsHubScreen />}
      detail={<FleetDetailView id={key} />}
    />
  );
}
