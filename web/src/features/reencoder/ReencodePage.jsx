import { useStore } from '../../lib/store';
import { ProgressBar } from '../../shared/components/ProgressBar';
import { reencodeStore } from './reencode-store';
import { emptyText } from './reencode-labels';
import { useExportTarget } from './hooks/useExportTarget';
import { useReencodeActions } from './hooks/useReencodeActions';
import { useReencodeJobs } from './hooks/useReencodeJobs';
import { useReencodeView } from './hooks/useReencodeView';
import { ReencodeActions } from './components/ReencodeActions';
import { ReencodeFilters } from './components/ReencodeFilters';
import { ReencodeTable } from './components/ReencodeTable';
import './reencode-page.scss';

/** Page Réencoder : convertit des fichiers du serveur au format des Paramètres, en les remplaçant sur place. */
export function ReencodePage() {
  const store = useStore(reencodeStore);
  const target = useExportTarget();
  const { scanText, scanning, busy, batchTotal, batchProgress } = useReencodeJobs(store);
  const view = useReencodeView(store.items, target, busy);
  const actions = useReencodeActions({ visible: view.visible, busy, scanning });

  return (
    <div className="dk-reencode-page">
      <section className="dk-page">
        <h1 className="dk-page-title">Réencoder des fichiers</h1>
        <p className="dk-help sub">
          Ajoutez des fichiers MP3 : une correspondance Deezer est recherchée automatiquement, puis « Convertir »
          réextrait chaque titre au format des paramètres et remplace le fichier dans son dossier d'origine.
        </p>

        <ReencodeActions busy={busy} scanning={scanning} actions={actions} />

        {(scanning || store.matching > 0) && <ProgressBar value={null} />}
        {(busy || batchTotal > 0) && (
          <div className="global">
            <span className="dk-help">Progression globale de la conversion :</span>
            <ProgressBar value={batchProgress} large />
          </div>
        )}

        <ReencodeFilters
          hideTarget={view.hideTarget}
          onToggle={view.toggleHideTarget}
          shown={view.visible.length}
          total={store.items.length}
          target={target}
        />

        <ReencodeTable
          view={view}
          empty={emptyText(scanText, store.items.length)}
          onToggle={actions.toggle}
          onListen={actions.listen}
        />
        {scanning && view.visible.length > 0 && <p className="dk-help">{scanText}</p>}
      </section>
    </div>
  );
}
