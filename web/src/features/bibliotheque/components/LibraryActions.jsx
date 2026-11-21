import { cx } from '../../../lib/cx';

const MISSING_TIP =
  "Afficher aussi les titres que seul l'historique connaît, dont le fichier n'est plus sur le disque. Ceux qui sont archivés restent écoutables.";

/** Barre d'actions : cases, titres absents, suppression, corbeille, volet Archive, Actualiser. */
export function LibraryActions({
  onCheckAll,
  onUncheckAll,
  includeMissing,
  onIncludeMissing,
  archiveOpen,
  onToggleArchive,
  onRefresh,
  selectedCount = 0,
  onDeleteSelected,
  onOpenTrash,
}) {
  return (
    <div className="dk-actions">
      <button className="control-btn" onClick={onCheckAll}>
        Tout cocher
      </button>
      <button className="control-btn" onClick={onUncheckAll}>
        Tout décocher
      </button>
      <label className="dk-check" data-tip={MISSING_TIP}>
        <input
          type="checkbox"
          className="dk-check"
          checked={includeMissing}
          onChange={e => onIncludeMissing(e.target.checked)}
        />
        Titres absents du disque
      </label>
      <span className="dk-spacer" />
      {onDeleteSelected && (
        <button
          className="danger-btn"
          disabled={selectedCount === 0}
          onClick={onDeleteSelected}
          data-tip="Met les titres cochés à la corbeille (bibliothèque et disque USB), récupérables depuis la Corbeille"
        >
          Supprimer{selectedCount ? ` (${selectedCount})` : ''}
        </button>
      )}
      {onOpenTrash && (
        <button
          className="control-btn"
          onClick={onOpenTrash}
          data-tip="Titres supprimés, récupérables (durée réglable dans les Paramètres)"
        >
          Corbeille
        </button>
      )}
      <button
        className={cx('control-btn', archiveOpen && 'checked')}
        onClick={onToggleArchive}
        aria-pressed={archiveOpen}
      >
        {archiveOpen ? '▾' : '▸'} Archive
      </button>
      <button className="accent-btn" onClick={onRefresh}>
        Actualiser
      </button>
    </div>
  );
}
