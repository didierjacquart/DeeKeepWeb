import { isRouteErrorResponse, useRouteError } from 'react-router';

/**
 * Erreur inattendue dans une page : message lisible et rechargement proposé,
 * au lieu d'un écran vide. La coquille et le lecteur restent en place.
 */
export function RouteError() {
  const error = useRouteError();
  const detail = isRouteErrorResponse(error)
    ? `${error.status} ${error.statusText}`
    : error instanceof Error
      ? error.message
      : String(error);
  return (
    <section className="dk-page" role="alert">
      <h1 className="dk-page-title">Cette page a rencontré une erreur</h1>
      <p className="dk-error-text">{detail}</p>
      <p className="dk-help">
        Les tâches de fond continuent sur le serveur. Recharger la page relance l&apos;affichage sans rien perdre.
      </p>
      <div className="dk-actions">
        <button className="primary-btn" onClick={() => window.location.reload()}>
          Recharger
        </button>
      </div>
    </section>
  );
}
