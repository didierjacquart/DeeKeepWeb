/**
 * Barre d'actions : manipulation de la liste à gauche, puis tags, doublons, analyse et « Organiser ».
 * @param {{ o: ReturnType<typeof import('../hooks/useOrganizer').useOrganizer> }} props
 */
export function ActionBar({ o }) {
  const { busy, entries, checks, view, plan } = o;
  const empty = entries.length === 0;
  return (
    <div className="dk-actions bar">
      <button className="control-btn" onClick={() => checks.checkAll(true)} disabled={empty || busy}>
        Tout cocher
      </button>
      <button className="control-btn" onClick={() => checks.checkAll(false)} disabled={empty || busy}>
        Tout décocher
      </button>
      <button className="control-btn" onClick={view.toggleExpandAll} disabled={empty}>
        {view.allCollapsed ? 'Tout déplier' : 'Tout replier'}
      </button>
      <span className="dk-spacer" />
      {o.resume && (
        <button className="control-btn" onClick={() => void o.reopenReview()} disabled={busy}>
          Reprendre la revue des tags
        </button>
      )}
      <button
        className="control-btn"
        onClick={() => void o.lookupTags()}
        disabled={busy || o.lookupBusy || !plan || checks.lookupIndices.length === 0}
      >
        Compléter les tags
      </button>
      <button className="control-btn" onClick={() => void o.findDuplicates()} disabled={busy || entries.length < 2}>
        Rechercher les doublons
      </button>
      {o.scanning ? (
        <button className="accent-btn" onClick={o.stopScan}>
          Arrêter l'analyse
        </button>
      ) : (
        <button className="accent-btn" onClick={() => void o.analyze()} disabled={busy}>
          Analyser
        </button>
      )}
      {o.applying && (
        <button className="accent-btn" onClick={o.stopApply}>
          Arrêter
        </button>
      )}
      <button className="primary-btn" onClick={() => void o.apply()} disabled={!o.canApply}>
        Organiser
      </button>
    </div>
  );
}
