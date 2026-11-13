import { useLocalSearchParams } from 'expo-router';
import { ListDetailRoute } from '@/navigation/listDetail';
import { BlackBoxEntryScreen } from '@/screens/blackbox/BlackBoxEntryScreen';
import { MissionsHubScreen } from '@/screens/operations/MissionsHubScreen';

/**
 * /blackbox/:id: one black box entry. Opened on a window wide enough for list and detail, it shows the Missions hub's
 * Black Box list with this entry selected beside it; narrower windows show the entry alone.
 */
export default function Screen() {
  const params = useLocalSearchParams<{ id?: string }>();
  const key = String(params.id ?? '');
  return (
    <ListDetailRoute
      path={`/blackbox/${key}`}
      id={key}
      tab="blackbox"
      list={<MissionsHubScreen />}
      detail={<BlackBoxEntryScreen key={key} id={key} />}
    />
  );
}
