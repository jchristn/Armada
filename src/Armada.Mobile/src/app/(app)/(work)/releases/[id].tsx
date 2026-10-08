import { useLocalSearchParams } from 'expo-router';
import { ListDetailRoute } from '@/navigation/listDetail';
import { DeliveryHub } from '@/screens/delivery/DeliveryHub';
import { ReleaseDetailRoute } from '@/screens/delivery/ReleaseDetail';

/**
 * /releases/:id: one item. Opened on a window wide enough for list and detail, it shows the Delivery hub's Releases
 * list with this item selected beside it (deep links and notifications land with the list on the left); narrower
 * windows show the item alone.
 */
export default function Screen() {
  const params = useLocalSearchParams<{ id?: string }>();
  const key = String(params.id ?? '');
  return (
    <ListDetailRoute
      path={`/releases/${key}`}
      id={key === 'new' ? '' : key}
      tab="releases"
      list={<DeliveryHub />}
      detail={<ReleaseDetailRoute />}
    />
  );
}
