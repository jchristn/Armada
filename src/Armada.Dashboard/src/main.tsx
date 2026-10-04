import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import App from './App';
import { installDialogEnhancer } from './lib/dialogA11y';
import './index.css';
import './a11y.css';
import './responsive.css';

installDialogEnhancer();

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App />
  </StrictMode>,
);
