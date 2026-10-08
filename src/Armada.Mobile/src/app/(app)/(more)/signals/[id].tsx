import { useLocalSearchParams } from 'expo-router';
import { ListDetailRoute } from '@/navigation/listDetail';
import { ActivityHub } from '@/screens/activity/ActivityHub';
import { SignalDetail } from '@/screens/operations/SignalDetail';

/**
 * /signals/:id: one item. Opened on a window wide enough for list and detail, it shows the Activity hub's Signals
 * list with this item selected beside it (deep links and notifications land with the list on the left); narrower
 * windows show the item alone.
 */
export default function Screen() {
  const params = useLocalSearchParams<{ id?: string }>();
  const key = String(params.id ?? '');
  return (
    <ListDetailRoute
      path={`/signals/${key}`}
      id={key}
      tab="signals"
      list={<ActivityHub />}
      detail={<SignalDetail id={key} />}
    />
  );
}
