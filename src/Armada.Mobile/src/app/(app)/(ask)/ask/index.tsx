import { useLocalSearchParams } from 'expo-router';
import { AskScreen } from '../../../../screens/AskScreen';

/**
 * /ask: the new-conversation screen, or the last conversation on the first visit of an app session. `?prompt=`
 * opens a new conversation with that text in the composer (for example "Ask Armada about this failure").
 */
export default function AskIndexRoute() {
  const { prompt } = useLocalSearchParams<{ prompt?: string }>();
  return <AskScreen routeThreadId={null} prefill={typeof prompt === 'string' && prompt ? prompt : null} />;
}
