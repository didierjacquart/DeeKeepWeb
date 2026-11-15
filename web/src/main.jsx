import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { createBrowserRouter } from 'react-router';
import { RouterProvider } from 'react-router/dom';
import { setUnauthorizedHandler, startApolloCache } from './core/graphql/apollo';
import { routes } from './app/routes';
import './styles.scss';

const router = createBrowserRouter(routes);

// Session expirée pendant une requête : retour à l'écran de connexion.
setUnauthorizedHandler(() => void router.navigate('/login'));

// Le cache persisté est restauré avant le premier rendu : hors ligne, les pages
// s'affichent aussitôt avec les données de la dernière visite.
void startApolloCache().finally(() => {
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
