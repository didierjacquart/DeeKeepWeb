import { useMemo, useState } from 'react';
import { useMobile } from '../../lib/hooks/useMediaQuery';
import { DefectFilters } from './components/DefectFilters';
import { FolderLine } from './components/FolderLine';
import { QualityActions } from './components/QualityActions';
import { QualityProgress } from './components/QualityProgress';
import { QualityRowMenu } from './components/QualityRowMenu';
import { QualityTable } from './components/QualityTable';
import { ThresholdsPanel } from './components/ThresholdsPanel';
import { useQualityEntries } from './hooks/useQualityEntries';
import { useQualityJobs } from './hooks/useQualityJobs';
import { useQualityThresholds } from './hooks/useQualityThresholds';
import { useQualityView } from './hooks/useQualityView';
import { useRedownload } from './hooks/useRedownload';
import { useRowSelection } from './hooks/useRowSelection';
import { useScanActions } from './hooks/useScanActions';
import { playRow } from './quality-row-actions';
import { qualityStats, redownloadSummary, scanProgress, statusLine } from './quality-summary';
import './quality-page.scss';

/**
 * Écran « Qualité » : mesure du signal de chaque fichier (tâche serveur « quality.scan »), verdict recalculé
 * par le serveur avec les seuils courants, retéléchargement des titres abîmés et réanalyse automatique.
 * La liste vit sur le serveur : quitter la page ou perdre la connexion n'interrompt rien, et les lignes
 * arrivent au fil de l'eau par l'événement « quality.updated ».
 */
export function QualityPage() {
  const compact = useMobile();
  const [panelOpen, setPanelOpen] = useState(false);
  const [menu, setMenu] = useState(null);

  const settings = useQualityThresholds();
  const { thresholds } = settings;
  const selection = useRowSelection();
  const entries = useQualityEntries({ onThresholds: settings.receive, onCleared: selection.clear });
  const { rows, folder } = entries;
  const tasks = useQualityJobs(rows);
  const view = useQualityView(rows, thresholds?.deep ?? true);
  const { list } = view;

  const stats = useMemo(() => qualityStats(rows), [rows]);
  const { selected } = selection;
  const selectedCount = useMemo(() => list.filter(r => selected.has(r.path)).length, [list, selected]);
  const summary = useMemo(() => redownloadSummary(tasks.redownloadJobs), [tasks.redownloadJobs]);

  const scan = useScanActions({
    folder,
    setFolder: entries.setFolder,
    deep: thresholds?.deep ?? true,
    busy: tasks.busy,
    scanJob: tasks.scanJob,
    rowCount: rows.size,
    onReset: () => {
      entries.reset();
      selection.clear();
    },
  });
  const redownload = useRedownload({ list, selection, downloading: tasks.downloading });
  const play = row => playRow(row, list);

  return (
    <section className="dk-quality-page dk-page">
      <h1 className="dk-page-title">Qualité des titres</h1>
      <p className="intro">
        Écoute le signal de chaque fichier — souffle continu, craquements, saturation, coupures au milieu du morceau —
        et compare sa durée à celle du morceau chez Deezer ou MusicBrainz : un fichier plus court ou plus long n'est pas
        la version attendue, et c'est le seul indice qu'un rip radio propre laisse derrière lui. Les résultats
        s'exploitent sans attendre la fin de l'analyse : cochez et retéléchargez au fil de l'eau. Les titres réextraits
        remplacent la version défectueuse — rangés selon les règles de l'écran « Organiser » — puis sont réanalysés pour
        vérifier que le défaut a bien disparu.
      </p>

      <FolderLine
        folder={folder}
        panelOpen={panelOpen}
        onTogglePanel={() => setPanelOpen(!panelOpen)}
        onChangeFolder={() => void scan.changeFolder()}
        busy={tasks.busy}
      />

      {panelOpen && (
        <ThresholdsPanel
          thresholds={thresholds}
          scanning={tasks.scanning}
          onDeep={settings.setDeep}
          onThreshold={settings.setThreshold}
        />
      )}

      <QualityActions
        scanning={tasks.scanning}
        busy={tasks.busy}
        downloading={tasks.downloading}
        selectedCount={selectedCount}
        onCheckAll={() => selection.checkAll(list)}
        onUncheckAll={() => selection.uncheckAll(list)}
        onClear={() => void scan.clearList()}
        onStop={() => void scan.stopScan()}
        onScan={() => void scan.startScan()}
        onRedownload={() => void redownload()}
      />

      <QualityProgress scanning={tasks.scanning} progress={scanProgress(tasks.scanJob)} redownload={summary} />

      <DefectFilters
        shown={view.shown}
        onToggle={view.toggleShown}
        includeClean={view.includeClean}
        onIncludeClean={view.setIncludeClean}
      />

      {entries.stale && <p className="stale">Connexion perdue : liste affichée depuis la mémoire de l'appareil.</p>}

      <QualityTable
        list={list}
        compact={compact}
        empty={stats.count === 0 ? 'Aucun résultat.' : 'Aucun titre ne correspond aux filtres.'}
        sort={view.sort}
        onSort={view.sortBy}
        selected={selected}
        redownloadById={tasks.redownloadById}
        onToggle={selection.toggle}
        onPlay={play}
        onMenu={(row, at) => setMenu({ ...at, row })}
      />

      <p className="status-line">
        {statusLine(tasks.scanJob, stats, entries.loaded, thresholds)}
        {tasks.refBusy && !tasks.scanning && <span> Recherche des durées de référence…</span>}
      </p>

      <QualityRowMenu menu={menu} onClose={() => setMenu(null)} onPlay={play} />
    </section>
  );
}
