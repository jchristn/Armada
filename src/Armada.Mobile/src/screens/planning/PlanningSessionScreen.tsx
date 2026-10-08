import { useLocalSearchParams, useRouter, type Href } from 'expo-router';
import { useLayout } from '../../navigation/useLayout';
import { PlanningScreen } from './PlanningScreen';
import { PlanningSessionView } from './PlanningSessionView';
import { usePlanningCatalog } from './usePlanningCatalog';

/**
 * /planning/:id. Phones show the session on its own (back returns to the list); tablets show the Planning list with
 * this session open beside it, like the dashboard's page. `?prompt=` drafts the first message.
 */
export function PlanningSessionScreen() {
  const { id, prompt } = useLocalSearchParams<{ id: string; prompt?: string }>();
  const { isTablet } = useLayout();
  if (isTablet) return <PlanningScreen key={id} initialSessionId={id} />;
  return <PhoneSession id={id} prompt={prompt ?? null} />;
}

function PhoneSession({ id, prompt }: { id: string; prompt: string | null }) {
  const router = useRouter();
  const catalog = usePlanningCatalog();
  const leave = () => {
    if (router.canGoBack()) router.back();
    else router.replace('/planning' as Href);
  };
  return <PlanningSessionView key={id} sessionId={id} catalog={catalog} initialComposer={prompt} onClosed={leave} />;
}
