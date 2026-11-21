import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { createBrowserRouter } from 'react-router';
import { RouterProvider } from 'react-router/dom';
import { startResponseCache } from './core/api/api';
import { setForbiddenHandler, setUnauthorizedHandler } from './core/api/http';
import { auth } from './core/auth/auth';
import { notify } from './core/ui/notify';
import { routes } from './app/routes';
import './styles.scss';

const router = createBrowserRouter(routes);

// Session expirée pendant une requête : retour à l'écran de connexion.
setUnauthorizedHandler(() => void router.navigate('/login'));

// Action refusée (droits changés par un administrateur) : message, puis droits relus
// pour que la navigation et les boutons suivent.
setForbiddenHandler(message => {
  notify.error(message);
  void auth.refresh();
});

// Le cache persisté est restauré avant le premier rendu : hors ligne, les pages
// s'affichent aussitôt avec les données de la dernière visite.
void startResponseCache().finally(() => {
  createRoot(document.getElementById('root')).render(
    <StrictMode>
      <RouterProvider router={router} />
    </StrictMode>,
  );
});

// Coquille, scripts et jaquettes mis en cache : l'application s'ouvre sans réseau.
if (import.meta.env.PROD) {
  void import('virtual:pwa-register').then(({ registerSW }) => registerSW({ immediate: false }));
}
