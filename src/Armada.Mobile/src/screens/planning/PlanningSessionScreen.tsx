import { useLocalSearchParams, useRouter, type Href } from 'expo-router';
import { useState } from 'react';
import { useLayout } from '../../navigation/useLayout';
import { PlanningScreen } from './PlanningScreen';
import { PlanningSessionView } from './PlanningSessionView';
import { usePlanningCatalog } from './usePlanningCatalog';

/**
 * /planning/:id. Narrow windows show the session on its own (back returns to the list); windows wide enough for
 * list and detail show the Planning list with this session open beside it, like the dashboard's page. Decided when
 * the route opens, so a later resize re-lays out the same screen instead of swapping it (the session's draft and
 * scroll are kept). `?prompt=` drafts the first message.
 */
export function PlanningSessionScreen() {
  const { id, prompt } = useLocalSearchParams<{ id: string; prompt?: string }>();
  const { split } = useLayout();
  const [asList] = useState(split);
  if (asList) return <PlanningScreen key={id} initialSessionId={id} />;
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
