/**
 * Une recherche : requête, catégorie, pochette du premier résultat, « Relancer » et suppression.
 * `cover` : undefined en attente (« ⏳ »), null sans pochette (« ♪ »), sinon l'URL de l'image.
 */
export function SearchRow({ search, cover, onRelaunch, onDelete }) {
  return (
    <div className="dk-row g-search">
      <span className="q" data-tip={search.query || undefined}>
        {search.query}
      </span>
      <span className="dk-hide-mobile dk-faint">Recherche Globale</span>
      <span>
        {cover ? (
          <img className="dk-cover thumb" src={cover} alt="" loading="lazy" />
        ) : (
          <span className="dk-cover thumb">{cover === undefined ? '⏳' : '♪'}</span>
        )}
      </span>
      <span className="acts">
        <button className="primary-btn small" onClick={onRelaunch}>
          Relancer
        </button>
        <button
          className="icon-btn"
          onClick={onDelete}
          aria-label="Supprimer cette recherche"
          data-tip="Retirer de l'historique"
        >
          ✕
        </button>
      </span>
    </div>
  );
}
