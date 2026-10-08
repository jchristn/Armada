import { useLocalSearchParams } from 'expo-router';
import { FleetDetailView } from '../../../../screens/fleets/FleetDetailView';

/** /fleets/:id: one fleet and its vessels. */
export default function FleetRoute() {
  const { id } = useLocalSearchParams<{ id: string }>();
  return <FleetDetailView id={String(id ?? '')} />;
}
