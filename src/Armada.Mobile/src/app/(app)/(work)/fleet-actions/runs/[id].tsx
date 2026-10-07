import { useLocalSearchParams } from 'expo-router';
import { FleetActionRunDetailView } from '../../../../../screens/fleetActions/FleetActionRunDetailView';

/** /fleet-actions/runs/:id: one fleet action run and its targets. */
export default function FleetActionRunRoute() {
  const { id } = useLocalSearchParams<{ id: string }>();
  return <FleetActionRunDetailView id={String(id ?? '')} />;
}
