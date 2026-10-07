import { useLocalSearchParams } from 'expo-router';
import { VoyageDetail } from '@/screens/operations/VoyageDetail';

export default function Screen() {
  const { id } = useLocalSearchParams<{ id: string }>();
  return <VoyageDetail id={String(id ?? '')} />;
}
