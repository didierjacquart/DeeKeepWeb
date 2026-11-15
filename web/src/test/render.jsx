import { render } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { createMemoryRouter } from 'react-router';
import { RouterProvider } from 'react-router/dom';
import { Root } from '../app/Root';

/**
 * Rend un composant dans un routeur en mémoire, avec les couches communes
 * (boîtes de dialogue, infobulles, messages) comme dans l'application.
 *
 * @param {import('react').ReactElement} ui
 * @param {{ route?: string, path?: string, extraRoutes?: object[] }} [options]
 *   `route` : adresse affichée ; `path` : motif de la route qui porte `ui` ;
 *   `extraRoutes` : autres pages joignables (pour vérifier une navigation).
 */
export function renderWithRouter(ui, { route = '/', path = '*', extraRoutes = [] } = {}) {
  const router = createMemoryRouter([{ element: <Root />, children: [{ path, element: ui }, ...extraRoutes] }], {
    initialEntries: [route],
  });
  const user = userEvent.setup();
  return { router, user, ...render(<RouterProvider router={router} />) };
}

/** Comme {@link renderWithRouter}, pour l'arbre de routes complet de l'application. */
export function renderRoutes(routes, route = '/') {
  const router = createMemoryRouter(routes, { initialEntries: [route] });
  const user = userEvent.setup();
  return { router, user, ...render(<RouterProvider router={router} />) };
}

/** Fabrique une tâche de fond complète pour les tests (les champs absents prennent une valeur neutre). */
export function makeJob(fields = {}) {
  return {
    id: 'job-1',
    service: 'library',
    type: 'library.scan',
    title: null,
    status: 'RUNNING',
    progress: 0,
    done: 0,
    failed: 0,
    total: 0,
    message: null,
    error: null,
    attempts: 0,
    groupKey: null,
    result: null,
    createdAt: 1,
    updatedAt: 1,
    startedAt: null,
    finishedAt: null,
    ...fields,
  };
}
