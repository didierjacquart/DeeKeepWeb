import { useMemo } from 'react';
import { QueueActions } from './components/QueueActions';
import { QueueStats } from './components/QueueStats';
import { QueueTable } from './components/QueueTable';
import { useDownloadQueue } from './hooks/useDownloadQueue';
import { useQueueActions } from './hooks/useQueueActions';
import { useQueuePresence } from './hooks/useQueuePresence';
import { useRowSelection } from './hooks/useRowSelection';
import { useSortState } from './hooks/useSortState';
import { eligibleRows, globalProgress } from './music-rows';
import { sortRows } from './sort-rows';
import './music-page.scss';

/**
 * Page Musique : la file d'extraction. Chaque ligne « En attente » est conservée
 * sur le serveur ; « Extraire » en fait une tâche « download » (durable, reprise
 * après redémarrage, suivie ici par abonnement). Fermer la page ou le navigateur
 * n'arrête rien.
 */
export function MusicPage() {
  const { rows, rowsRef, error, stale, reload } = useDownloadQueue();
  const verdicts = useQueuePresence(rows);
  const { selected, toggle, selectAll, clear } = useRowSelection(rows);
  const { sortKey, sortAsc, sort } = useSortState();

  const sorted = useMemo(() => sortRows(rows, sortKey, sortAsc), [rows, sortKey, sortAsc]);
  const failedRows = useMemo(() => rows.filter(r => r.failed), [rows]);
  const eligible = useMemo(() => eligibleRows(rows, selected), [rows, selected]);

  const actions = useQueueActions({ rows, rowsRef, eligible, failedRows, selected, clearSelection: clear, reload });
  const doneCount = actions.purged + rows.filter(r => r.succeeded).length;

  return (
    <div className="dk-music-page">
      <section className="dk-page">
        <QueueStats doneCount={doneCount} failedCount={failedRows.length} progress={globalProgress(rows)} />

        <QueueActions
          hasSelection={selected.size > 0}
          canRetry={failedRows.length > 0}
          canExtract={eligible.length > 0}
          onSelectAll={selectAll}
          onRemoveSelection={() => void actions.removeSelection()}
          onPurge={() => void actions.purge()}
          onRetry={() => void actions.retry()}
          onSearch={() => void actions.search()}
          onExtract={() => void actions.extract()}
        />

        {error && <p className="dk-error-text">{error}</p>}
        {stale && <p className="dk-help">Hors ligne : dernier état connu de la liste.</p>}

        <QueueTable
          rows={sorted}
          selected={selected}
          verdicts={verdicts}
          sortKey={sortKey}
          sortAsc={sortAsc}
          onSort={sort}
          onToggle={toggle}
          onCancel={actions.cancel}
        />
      </section>
    </div>
  );
}
