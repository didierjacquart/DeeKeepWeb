import { formatBytes } from '../../../shared/format';
import { useLibraryJobs } from '../hooks/useLibraryJobs';
import { LibraryJobStatus } from './LibraryJobStatus';

/** Base de données : emplacement, tailles, tables ; relecture de la bibliothèque et import desktop. */
export function DatabaseCard({ db }) {
  const { scanJob, importJob, scanning, importing, rescan, importDesktop } = useLibraryJobs(db?.desktopDataDir ?? '');

  return (
    <div className="dk-card form">
      {db ? (
        <>
          <p className="dk-help">
            <strong>{db.path}</strong> — {formatBytes(db.sizeBytes)} · {db.historyCount} téléchargements dans
            l'historique · jaquettes {formatBytes(db.coverBytes)} · index de présence :{' '}
            {db.presenceReady ? db.presenceFiles + ' fichiers' : 'en cours de lecture…'}
          </p>
          <div className="tables">
            {db.tables.map(t => (
              <span key={t.table} className="dk-chip">
                {t.table} : {t.rows}
                {t.deleted ? ' (+' + t.deleted + ' supprimées)' : ''}
              </span>
            ))}
          </div>
        </>
      ) : (
        <p className="dk-faint">Informations de la base indisponibles.</p>
      )}

      <LibraryJobStatus job={scanJob} active={scanning} doneLabel="Dernière relecture" />
      <LibraryJobStatus job={importJob} active={importing} doneLabel="Dernier import" />

      <div className="dk-actions">
        <span className="dk-spacer"></span>
        <button
          className="accent-btn"
          disabled={importing}
          onClick={() => void importDesktop()}
          data-tip="Reprend réglages et historique de l'application desktop (deekeep_config.json)"
        >
          Importer depuis l'application desktop
        </button>
        <button
          className="primary-btn"
          disabled={scanning}
          onClick={() => void rescan()}
          data-tip="Parcourt le dossier de sortie : seuls les fichiers nouveaux ou modifiés sont relus"
        >
          Relire la bibliothèque
        </button>
      </div>
    </div>
  );
}
