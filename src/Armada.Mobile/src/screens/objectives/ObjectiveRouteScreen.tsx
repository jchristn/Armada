import { useLocalSearchParams } from 'expo-router';
import { ObjectiveDetailScreen } from './ObjectiveDetailScreen';

function first(value: string | string[] | undefined): string {
  return Array.isArray(value) ? value[0] ?? '' : value ?? '';
}

/** /backlog/:id and /objectives/:id ('new' creates; ?vesselId= prefills; ?refinementSessionId= opens a transcript). */
export function ObjectiveRouteScreen() {
  const params = useLocalSearchParams<{ id?: string; vesselId?: string; refinementSessionId?: string }>();
  const id = first(params.id);
  return (
    <ObjectiveDetailScreen
      key={id}
      id={id}
      prefillVesselId={first(params.vesselId) || null}
      refinementSessionId={first(params.refinementSessionId) || null}
    />
  );
}
