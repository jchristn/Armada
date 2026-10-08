import { useLocalSearchParams } from 'expo-router';
import { ListDetailRoute } from '@/navigation/listDetail';
import { FleetActionRunDetailView } from '@/screens/fleetActions/FleetActionRunDetailView';
import { FleetActionsScreen } from '@/screens/fleetActions/FleetActionsScreen';

/**
 * /fleet-actions/runs/:id: one item. Opened on a window wide enough for list and detail, it shows Fleet Actions'
 * Runs list with this item selected beside it (deep links and notifications land with the list on the left);
 * narrower windows show the item alone.
 */
export default function Screen() {
  const params = useLocalSearchParams<{ id?: string }>();
  const key = String(params.id ?? '');
  return (
    <ListDetailRoute
      path={`/fleet-actions/runs/${key}`}
      id={key}
      tab="runs"
      list={<FleetActionsScreen />}
      detail={<FleetActionRunDetailView id={key} />}
    />
  );
}
