import { useLocalSearchParams } from 'expo-router';
import { VesselOnboardingScreen } from '../../../../../screens/vessels/VesselOnboardingScreen';

/** /vessels/:id/onboarding: the vessel onboarding checklist. */
export default function VesselOnboardingRoute() {
  const { id } = useLocalSearchParams<{ id: string }>();
  return <VesselOnboardingScreen id={String(id ?? '')} />;
}
