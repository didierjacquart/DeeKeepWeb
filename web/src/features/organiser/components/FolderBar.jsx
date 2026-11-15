/** Dossier analysé, ouverture du volet Filtres et choix d'un autre dossier. */
export function FolderBar({ root, filtersOpen, busy, onToggleFilters, onChooseFolder }) {
  return (
    <div className="folder-line">
      <span className="folder" data-tip={root || undefined}>
        Dossier analysé : {root || '—'}
      </span>
      <span className="dk-spacer" />
      <button className="control-btn" aria-pressed={filtersOpen} onClick={onToggleFilters}>
        Filtres {filtersOpen ? '▴' : '▾'}
      </button>
      <button className="control-btn" onClick={onChooseFolder} disabled={busy}>
        Modifier le dossier
      </button>
    </div>
  );
}
