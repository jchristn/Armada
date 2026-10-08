import { useLocalSearchParams } from 'expo-router';
import { ListDetailRoute } from '@/navigation/listDetail';
import { prefillQuery } from '@/resource/links';
import { DeliveryHub } from '@/screens/delivery/DeliveryHub';
import { RunbookDetailRoute } from '@/screens/delivery/RunbookDetail';
import { executionPrefillFrom, executionPrefillQuery } from '@/screens/delivery/runbookForm';

/**
 * /runbooks/:id ('new' creates; `?executionId=` opens an execution). Opened on a window wide enough for list and
 * detail, it shows the Delivery hub's Runbooks list with this runbook selected beside it; narrower windows, a new
 * runbook, and an execution link show the page alone.
 */
export default function Screen() {
  const params = useLocalSearchParams<Record<string, string>>();
  const key = String(params.id ?? '');
  // The Runbooks list carries the execution prefill in its links; the same query makes the same path.
  const carry = prefillQuery(executionPrefillQuery(executionPrefillFrom(params)));
  return (
    <ListDetailRoute
      path={`/runbooks/${key}${carry}`}
      id={key === 'new' || params.executionId ? '' : key}
      tab="runbooks"
      list={<DeliveryHub />}
      detail={<RunbookDetailRoute />}
    />
  );
}
