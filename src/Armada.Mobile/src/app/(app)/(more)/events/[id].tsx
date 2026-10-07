import { useLocalSearchParams } from 'expo-router';
import { EventDetail } from '@/screens/operations/EventDetail';

/** /events/:id: one event (W2.4). */
export default function Screen() {
  const { id } = useLocalSearchParams<{ id: string }>();
  return <EventDetail id={String(id ?? '')} />;
}
