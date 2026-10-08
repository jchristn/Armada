import { useLocalSearchParams } from 'expo-router';
import { ListDetailRoute } from '@/navigation/listDetail';
import { CaptainDetailScreen } from '@/screens/captains/CaptainDetailScreen';
import { CaptainsHubScreen } from '@/screens/captains/CaptainsHubScreen';

/**
 * /captains/:id: one item. Opened on a window wide enough for list and detail, it shows the Captains hub's Captains
 * list with this item selected beside it (deep links and notifications land with the list on the left); narrower
 * windows show the item alone.
 */
export default function Screen() {
  const params = useLocalSearchParams<{ id?: string }>();
  const key = String(params.id ?? '');
  return (
    <ListDetailRoute
      path={`/captains/${key}`}
      id={key}
      tab="captains"
      list={<CaptainsHubScreen />}
      detail={<CaptainDetailScreen id={key} />}
    />
  );
}
