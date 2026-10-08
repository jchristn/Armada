import { useLocalSearchParams } from 'expo-router';
import { ListDetailRoute } from '../../navigation/listDetail';
import { ObjectiveDetailScreen } from './ObjectiveDetailScreen';
import { backlogItemPath } from './objectiveLinks';
import { ObjectivesScreen } from './ObjectivesScreen';

function first(value: string | string[] | undefined): string {
  return Array.isArray(value) ? value[0] ?? '' : value ?? '';
}

/**
 * /backlog/:id and /objectives/:id ('new' creates; ?vesselId= prefills; ?refinementSessionId= opens a transcript).
 * Opened on a window wide enough for list and detail, an existing item shows in the Backlog list's detail pane.
 */
export function ObjectiveRouteScreen() {
  const params = useLocalSearchParams<{ id?: string; vesselId?: string; refinementSessionId?: string }>();
  const id = first(params.id);
  const refinementSessionId = first(params.refinementSessionId) || null;
  return (
    <ListDetailRoute
      path={backlogItemPath(id)}
      id={id === 'new' || refinementSessionId ? '' : id}
      list={<ObjectivesScreen />}
      detail={(
        <ObjectiveDetailScreen
          key={id}
          id={id}
          prefillVesselId={first(params.vesselId) || null}
          refinementSessionId={refinementSessionId}
        />
      )}
    />
  );
}
