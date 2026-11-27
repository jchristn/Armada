import { useLocalSearchParams } from 'expo-router';
import { ListDetailRoute } from '@/navigation/listDetail';
import { CaptainsHubScreen } from '@/screens/captains/CaptainsHubScreen';
import { ModelProfileDetailScreen } from '@/screens/captains/ModelProfileDetailScreen';

/**
 * /model-profiles/:id: one model profile, read-only. Opened on a window wide enough for list and detail, it shows the
 * Captains hub's Model Profiles list with this profile selected beside it; narrower windows show the profile alone.
 */
export default function Screen() {
  const params = useLocalSearchParams<{ id?: string }>();
  const key = String(params.id ?? '');
  return (
    <ListDetailRoute
      path={`/model-profiles/${key}`}
      id={key}
      tab="profiles"
      list={<CaptainsHubScreen />}
      detail={<ModelProfileDetailScreen id={key} />}
    />
  );
}
