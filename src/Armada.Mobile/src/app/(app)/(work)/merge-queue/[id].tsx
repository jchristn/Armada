import { useLocalSearchParams } from 'expo-router';
import { ListDetailRoute } from '@/navigation/listDetail';
import { MergeEntryDetail } from '@/screens/operations/MergeEntryDetail';
import { MissionsHubScreen } from '@/screens/operations/MissionsHubScreen';

/**
 * /merge-queue/:id: one item. Opened on a window wide enough for list and detail, it shows the Missions hub's Merge
 * Queue list with this item selected beside it (deep links and notifications land with the list on the left);
 * narrower windows show the item alone.
 */
export default function Screen() {
  const params = useLocalSearchParams<{ id?: string }>();
  const key = String(params.id ?? '');
  return (
    <ListDetailRoute
      path={`/merge-queue/${key}`}
      id={key}
      tab="merge-queue"
      list={<MissionsHubScreen />}
      detail={<MergeEntryDetail id={key} />}
    />
  );
}
