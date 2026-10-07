import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import App from './App';
import { configureClient } from './api/client';
import { installDialogEnhancer } from './lib/dialogA11y';
import { browserPlatform } from './platform/browserPlatform';
import './index.css';
import './a11y.css';
import './responsive.css';

// The shared API client is host-agnostic; the dashboard supplies its server origin and browser services here.
configureClient({ baseUrl: import.meta.env.VITE_ARMADA_SERVER_URL || '', platform: browserPlatform });
installDialogEnhancer();

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App />
  </StrictMode>,
);
