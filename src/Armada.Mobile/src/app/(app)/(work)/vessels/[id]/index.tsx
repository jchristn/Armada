import { useLocalSearchParams, useRouter } from 'expo-router';
import { useCallback } from 'react';
import { VesselDetailView } from '../../../../../screens/vessels/VesselDetailView';

/** /vessels/:id: the vessel page (`?edit=1` opens the edit form, as after Duplicate). */
export default function VesselDetailRoute() {
  const { id, edit } = useLocalSearchParams<{ id: string; edit?: string }>();
  const router = useRouter();
  const clearEdit = useCallback(() => router.setParams({ edit: undefined }), [router]);
  return <VesselDetailView id={String(id ?? '')} openEdit={edit === '1'} onEditOpened={clearEdit} />;
}
