import { useLocalSearchParams } from 'expo-router';
import { AskScreen } from '../../../../screens/AskScreen';

/** /ask/:threadId: one conversation (deep links, notifications, and links from other screens). */
export default function AskThreadRoute() {
  const { threadId } = useLocalSearchParams<{ threadId?: string }>();
  return <AskScreen routeThreadId={typeof threadId === 'string' && threadId ? threadId : null} />;
}
