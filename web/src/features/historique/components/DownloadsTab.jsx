import { clearHistory, playDownload, purgeMissing } from '../history-actions';
import { downloadSortMark } from '../history-sort';
import { openCoverPopup } from '../open-cover-popup';
import { DownloadRow } from './DownloadRow';
import { SortHeader } from '../../../shared/components/SortHeader';

const COLUMNS = [
  { key: 'title', label: 'Titre' },
  { key: 'artist', label: 'Artiste', className: 'dk-hide-mobile' },
  { key: 'album', label: 'Album', className: 'dk-hide-mobile' },
  { key: 'path', label: 'Chemin local', className: 'dk-hide-mobile' },
];

/** Onglet des téléchargements réussis : écoute, jaquette, purge des introuvables, vidage confirmé. */
export function DownloadsTab({ view, count, loading }) {
  const { sort, shownDownloads } = view;
  return (
    <>
      <div className="dk-actions">
        <span className="dk-spacer" />
        <button
          className="accent-btn"
          data-tip="Retirer de l'historique les téléchargements dont le fichier n'existe plus sur le disque"
          onClick={() => void purgeMissing()}
        >
          Purger les fichiers introuvables
        </button>
        <button
          className="danger-btn"
          data-tip="Retirer toutes les entrées de l'historique (les fichiers du disque sont conservés)"
          disabled={!count}
          onClick={() => void clearHistory()}
        >
          Vider l'historique
        </button>
      </div>
      <div className="dk-table">
        <div className="dk-thead g-dl" role="row">
          {COLUMNS.map(c => (
            <SortHeader
              key={c.key}
              label={c.label}
              mark={downloadSortMark(sort, c.key)}
              sorted={sort?.key === c.key}
              className={c.className}
              onSort={() => view.sortBy(c.key)}
            />
          ))}
          <span>Jaquette</span>
          <span>Écoute</span>
        </div>
        {shownDownloads.map(e => (
          <DownloadRow
            key={e.path}
            entry={e}
            onPlay={() => playDownload(e, shownDownloads)}
            onZoom={() => void openCoverPopup(e)}
          />
        ))}
        {!shownDownloads.length && (
          <div className="dk-empty">{loading ? 'Chargement…' : "Aucun téléchargement dans l'historique."}</div>
        )}
      </div>
    </>
  );
}
