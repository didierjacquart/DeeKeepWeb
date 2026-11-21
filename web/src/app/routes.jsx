import { redirect } from 'react-router';
import { redirectHome, requirePage, requireSession } from '../core/auth/auth';
import { LoginPage } from '../features/login/LoginPage';
import { Shell } from '../layout/shell/Shell';
import { Root } from './Root';
import { RouteError } from './RouteError';

/** Page chargée à la demande (un fichier JavaScript par page). */
const lazyPage = (load, pick) => async () => ({ Component: pick(await load()) });

/** Route d'une page soumise aux droits du compte : son nom d'adresse est aussi sa clé de droits. */
const page = (path, load, pick) => ({ path, loader: requirePage(path), lazy: lazyPage(load, pick) });

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
              { index: true, loader: redirectHome },
              page(
                'musique',
                () => import('../features/musique/MusicPage'),
                m => m.MusicPage,
              ),
              page(
                'bibliotheque',
                () => import('../features/bibliotheque/LibraryPage'),
                m => m.LibraryPage,
              ),
              page(
                'historique',
                () => import('../features/historique/HistoryPage'),
                m => m.HistoryPage,
              ),
              page(
                'reencoder',
                () => import('../features/reencoder/ReencodePage'),
                m => m.ReencodePage,
              ),
              page(
                'organiser',
                () => import('../features/organiser/OrganizePage'),
                m => m.OrganizePage,
              ),
              page(
                'qualite',
                () => import('../features/qualite/QualityPage'),
                m => m.QualityPage,
              ),
              page(
                'parametres',
                () => import('../features/parametres/SettingsPage'),
                m => m.SettingsPage,
              ),
              page(
                'diagnostic',
                () => import('../features/diagnostic/DiagnosticsPage'),
                m => m.DiagnosticsPage,
              ),
              page(
                'administration',
                () => import('../features/administration/AdministrationPage'),
                m => m.AdministrationPage,
              ),
              {
                path: 'aucun-acces',
                lazy: lazyPage(
                  () => import('./NoAccess'),
                  m => m.NoAccess,
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
