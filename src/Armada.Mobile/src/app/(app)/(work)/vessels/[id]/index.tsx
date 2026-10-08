import { useLocalSearchParams, useRouter } from 'expo-router';
import { useCallback } from 'react';
import { ListDetailRoute } from '../../../../../navigation/listDetail';
import { VesselDetailView } from '../../../../../screens/vessels/VesselDetailView';
import { VesselsHubScreen } from '../../../../../screens/vessels/VesselsHubScreen';
import { vesselLinks } from '../../../../../screens/vessels/vesselLinks';

/**
 * /vessels/:id: the vessel page (`?edit=1` opens the edit form, as after Duplicate). Opened on a window wide enough
 * for list and detail (and not to edit), it shows the Vessels hub with this vessel selected beside the list.
 */
export default function VesselDetailRoute() {
  const { id, edit } = useLocalSearchParams<{ id: string; edit?: string }>();
  const router = useRouter();
  const clearEdit = useCallback(() => router.setParams({ edit: undefined }), [router]);
  const key = String(id ?? '');
  return (
    <ListDetailRoute
      path={vesselLinks.detail(key)}
      id={edit === '1' ? '' : key}
      tab="vessels"
      list={<VesselsHubScreen />}
      detail={<VesselDetailView id={key} openEdit={edit === '1'} onEditOpened={clearEdit} />}
    />
  );
}
