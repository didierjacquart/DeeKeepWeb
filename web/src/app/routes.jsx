import { redirect } from 'react-router';
import { requireSession } from '../core/auth/auth';
import { LoginPage } from '../features/login/LoginPage';
import { Shell } from '../layout/shell/Shell';
import { Root } from './Root';
import { RouteError } from './RouteError';

/** Page chargée à la demande (un fichier JavaScript par page). */
const page = (load, pick) => async () => ({ Component: pick(await load()) });

/** Arbre des routes de l'application (routeur du navigateur, ou en mémoire dans les tests). */
export const routes = [
  {
    element: <Root />,
    HydrateFallback: () => null,
    children: [
      { path: 'login', element: <LoginPage /> },
      { path: 'setup', element: <LoginPage /> },
      {
        path: '/',
        element: <Shell />,
        loader: requireSession,
        children: [
          {
            errorElement: <RouteError />,
            children: [
              { index: true, loader: () => redirect('/musique') },
              {
                path: 'musique',
                lazy: page(
                  () => import('../features/musique/MusicPage'),
                  m => m.MusicPage,
                ),
              },
              {
                path: 'bibliotheque',
                lazy: page(
                  () => import('../features/bibliotheque/LibraryPage'),
                  m => m.LibraryPage,
                ),
              },
              {
                path: 'historique',
                lazy: page(
                  () => import('../features/historique/HistoryPage'),
                  m => m.HistoryPage,
                ),
              },
              {
                path: 'reencoder',
                lazy: page(
                  () => import('../features/reencoder/ReencodePage'),
                  m => m.ReencodePage,
                ),
              },
              {
                path: 'organiser',
                lazy: page(
                  () => import('../features/organiser/OrganizePage'),
                  m => m.OrganizePage,
                ),
              },
              {
                path: 'qualite',
                lazy: page(
                  () => import('../features/qualite/QualityPage'),
                  m => m.QualityPage,
                ),
              },
              {
                path: 'parametres',
                lazy: page(
                  () => import('../features/parametres/SettingsPage'),
                  m => m.SettingsPage,
                ),
              },
              {
                path: 'diagnostic',
                lazy: page(
                  () => import('../features/diagnostic/DiagnosticsPage'),
                  m => m.DiagnosticsPage,
                ),
              },
            ],
          },
        ],
      },
      { path: '*', loader: () => redirect('/') },
    ],
  },
];
