import { useLocalSearchParams } from 'expo-router';
import { VesselHistoryScreen } from '../../../../../screens/vessels/history/VesselHistoryScreen';

/** /vessels/:id/history: View History (commit heatmap and timeline). */
export default function VesselHistoryRoute() {
  const { id } = useLocalSearchParams<{ id: string }>();
  return <VesselHistoryScreen id={String(id ?? '')} />;
}
