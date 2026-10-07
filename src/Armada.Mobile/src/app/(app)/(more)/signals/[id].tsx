import { useLocalSearchParams } from 'expo-router';
import { SignalDetail } from '@/screens/operations/SignalDetail';

/** /signals/:id: one signal (W2.4). */
export default function Screen() {
  const { id } = useLocalSearchParams<{ id: string }>();
  return <SignalDetail id={String(id ?? '')} />;
}
