import { useLocalSearchParams } from 'expo-router';
import { WORKSPACE_PANELS, WorkspaceScreen, type WorkspacePanel } from '../../../../../screens/workspace/WorkspaceScreen';

/** /workspace/:vesselId/:panel: a vessel's workspace opened on a panel (files, search, changes, diff, terminal). */
export default function WorkspacePanelRoute() {
  const { vesselId, panel } = useLocalSearchParams<{ vesselId: string; panel: string }>();
  const initial = (WORKSPACE_PANELS as string[]).includes(String(panel)) ? (panel as WorkspacePanel) : 'files';
  return <WorkspaceScreen key={`${vesselId}/${initial}`} vesselId={String(vesselId ?? '')} initialPanel={initial} />;
}
