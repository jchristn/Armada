import { useLocalSearchParams } from 'expo-router';
import { ListDetailRoute } from '@/navigation/listDetail';
import { CheckRunDetailRoute } from '@/screens/delivery/CheckRunDetail';
import { DeliveryHub } from '@/screens/delivery/DeliveryHub';

/**
 * /checks/:id: one item. Opened on a window wide enough for list and detail, it shows the Delivery hub's Checks list
 * with this item selected beside it (deep links and notifications land with the list on the left); narrower windows
 * show the item alone.
 */
export default function Screen() {
  const params = useLocalSearchParams<{ id?: string }>();
  const key = String(params.id ?? '');
  return (
    <ListDetailRoute
      path={`/checks/${key}`}
      id={key === 'new' ? '' : key}
      tab="checks"
      list={<DeliveryHub />}
      detail={<CheckRunDetailRoute />}
    />
  );
}
