import { useLocalSearchParams } from 'expo-router';
import { WorkspaceScreen } from '../../../../../screens/workspace/WorkspaceScreen';

/** /workspace/:vesselId: a vessel's workspace (Files panel). */
export default function WorkspaceRoute() {
  const { vesselId } = useLocalSearchParams<{ vesselId: string }>();
  return <WorkspaceScreen key={vesselId} vesselId={String(vesselId ?? '')} />;
}
