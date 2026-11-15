import { useServices } from './hooks/useServices';
import { DeviceCard } from './components/DeviceCard';
import { ServiceCard } from './components/ServiceCard';
import { ToolsCard } from './components/ToolsCard';
import './diagnostics-page.scss';

/**
 * Diagnostic : état de chaque microservice (joignable, mémoire, tâches, boîte d'envoi, liens
 * d'événements), outils externes avec leurs versions, dossier d'enregistrement, et état de la
 * liaison du client (connexion, file d'actions hors ligne, titres épinglés).
 */
export function DiagnosticsPage() {
  const { services, downloadDir, loading, stale, error, refresh } = useServices();
  const reachable = services.filter(s => s.reachable).length;

  return (
    <section className="dk-page dk-diagnostics-page">
      <h1 className="dk-page-title">Diagnostic</h1>

      <div className="dk-actions">
        <span className="dk-faint">
          {reachable} / {services.length} services joignables
        </span>
        <span className="dk-spacer"></span>
        <button className="accent-btn" onClick={refresh}>
          Actualiser
        </button>
      </div>

      {stale && <p className="dk-help dk-error-text">Hors ligne : état servi par le cache local, peut être périmé.</p>}
      {error && <p className="dk-error-text">{error}</p>}

      <div className="grid">
        {services.map(s => (
          <ServiceCard key={s.service} service={s} />
        ))}
        {!services.length && <div className="dk-empty">{loading ? 'Chargement…' : 'Aucun service.'}</div>}
      </div>

      <h2 className="sub">Outils</h2>
      <ToolsCard services={services} />

      <h2 className="sub">Enregistrement</h2>
      <div className="dk-card">
        <p className="dk-help">
          Dossier d'enregistrement actuel : <strong>{downloadDir || '—'}</strong>
        </p>
      </div>

      <h2 className="sub">Cet appareil</h2>
      <DeviceCard />
    </section>
  );
}
