import { cx } from '../../../lib/cx';
import { jobs } from '../../../core/jobs/jobs';
import { ProgressBar } from '../../../shared/components/ProgressBar';
import { formatBytes, formatWhen } from '../../../shared/format';
import { useUsbSync } from '../hooks/useUsbSync';

/**
 * Disque USB : copie de la bibliothèque tenue à jour dans les deux sens (au branchement, chaque
 * nuit, ou à la demande). Incrémental : seule une suppression faite dans l'application se propage.
 */
export function UsbSyncCard() {
  const { status, job, running, loading, error, run } = useUsbSync();

  if (loading) return <div className="dk-card form dk-faint">Lecture de l'état du disque USB…</div>;
  if (error) return <div className="dk-card form dk-error-text">État du disque USB indisponible : {error}</div>;
  if (!status?.enabled) {
    return (
      <div className="dk-card form">
        <p className="dk-faint">
          Synchronisation non configurée : renseignez <code>Sync:UsbRoot</code> (point de montage du disque) dans la
          configuration du serveur.
        </p>
      </div>
    );
  }

  const last = status.last;
  return (
    <div className="dk-card form usb-sync">
      <p className="dk-help">
        <span className={cx('dk-chip', status.usbPresent ? 'usb-ok' : 'usb-absent')}>
          {status.usbPresent ? 'Disque branché' : 'Disque absent'}
        </span>{' '}
        <strong>{status.usbRoot}</strong>
        {status.usbPresent && status.usbTotalBytes
          ? ` — ${formatBytes(status.usbFreeBytes)} libres sur ${formatBytes(status.usbTotalBytes)}`
          : ''}
        <br />
        Bibliothèque : <strong>{status.libraryRoot ?? 'inconnue'}</strong>
        {!status.libraryPresent && <span className="dk-error-text"> (inaccessible)</span>}
      </p>
      <p className="dk-faint">
        Les fichiers ajoutés d'un côté sont copiés de l'autre ; un fichier effacé à la main revient. Seule une
        suppression faite dans l'application (Bibliothèque → Supprimer) est reportée sur le disque. Synchronisation
        automatique au branchement et chaque nuit.
        {status.pendingDeletions > 0 && ` ${status.pendingDeletions} suppression(s) en attente du disque.`}
      </p>

      {running && job && (
        <>
          <ProgressBar value={job.total ? job.progress : null} label={job.message} />
          <button className="control-btn small" onClick={() => void jobs.cancel(job)}>
            Arrêter
          </button>
        </>
      )}
      {!running && job?.status === 'FAILED' && (
        <p className="dk-help dk-error-text">Dernier essai : {job.error ?? job.message}</p>
      )}
      {!running && last && (
        <p className={cx('dk-help', last.failed > 0 && 'dk-error-text')}>
          Dernière synchronisation ({formatWhen(last.at)}) : {last.message}
          {last.bytes > 0 && ` — ${formatBytes(last.bytes)} copiés`}
        </p>
      )}
      {!running && !last && status.usbPresent && <p className="dk-faint">Aucune synchronisation pour l'instant.</p>}
      {status.problem && !running && <p className="dk-help dk-error-text">{status.problem}</p>}

      <div className="dk-actions">
        <span className="dk-spacer"></span>
        <button
          className="primary-btn"
          disabled={running || !status.usbPresent || !status.libraryPresent}
          onClick={() => void run()}
          data-tip="Copie dans les deux sens ce qui manque ou a changé"
        >
          {running ? 'Synchronisation en cours…' : 'Synchroniser maintenant'}
        </button>
      </div>
    </div>
  );
}
