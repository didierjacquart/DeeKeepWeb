import { cx } from '../../../lib/cx';
import { formatBytes } from '../../../shared/format';
import { linkTip, serviceLabel, uptime } from '../diagnostic-labels';

/** Carte d'un microservice : joignable, version, mémoire, tâches, boîte d'envoi, liens d'événements. */
export function ServiceCard({ service: s }) {
  return (
    <article className={cx('dk-card svc', !s.reachable && 'down')}>
      <header>
        <span className={cx('dot', s.reachable && 'ok', !s.reachable && 'bad')}></span>
        <h2>{serviceLabel(s.service)}</h2>
        <span className={cx('dk-chip', s.reachable && 'ok', !s.reachable && 'err')}>
          {s.reachable ? 'En ligne' : 'Injoignable'}
        </span>
      </header>
      {s.info ? (
        <>
          <dl>
            <dt>Version</dt>
            <dd>
              {s.info.version} · démarré il y a {uptime(s.info.startedAt)}
            </dd>
            <dt>Mémoire</dt>
            <dd>
              {formatBytes(s.info.workingSetBytes)} (tas géré {formatBytes(s.info.managedHeapBytes)})
            </dd>
            <dt>Tâches actives</dt>
            <dd>{s.info.activeJobs}</dd>
            <dt>Boîte d'envoi</dt>
            <dd className={cx(s.info.outboxPending > 0 && 'dk-error-text')}>{s.info.outboxPending} en attente</dd>
          </dl>
          {s.info.links.length > 0 && (
            <div className="links">
              {s.info.links.map(l => (
                <span
                  key={l.producer}
                  className={cx('dk-chip', l.connected && 'ok', !l.connected && 'err')}
                  data-tip={linkTip(l)}
                >
                  événements ← {l.producer} : {l.connected ? 'connecté' : 'coupé'}
                </span>
              ))}
            </div>
          )}
        </>
      ) : (
        <p className="dk-error-text">{s.error}</p>
      )}
    </article>
  );
}
