/**
 * Ligne d'un dossier : repli, case (partielle si une partie seulement de ses fichiers est cochée), nom et
 * album préservé.
 * @param {{ node: import('../organize-tree').DirNode, state: 'empty' | 'none' | 'some' | 'all', collapsed: boolean, onToggleOpen: () => void, onToggleCheck: () => void }} props
 */
export function DirRow({ node, state, collapsed, onToggleOpen, onToggleCheck }) {
  return (
    <div className="dk-row node dir" style={{ '--depth': node.depth }}>
      <span className="c-name">
        <button className="twisty" onClick={onToggleOpen} aria-label={collapsed ? 'Déplier' : 'Replier'}>
          {collapsed ? '▸' : '▾'}
        </button>
        {state !== 'empty' && (
          <input
            type="checkbox"
            className="dk-check"
            checked={state === 'all'}
            ref={el => {
              if (el) el.indeterminate = state === 'some';
            }}
            onChange={onToggleCheck}
            aria-label={'Cocher ' + node.name}
          />
        )}
        <span className="label dir-name" data-tip={node.name || undefined}>
          {node.name}
        </span>
      </span>
      <span className="c-artist" />
      <span className="c-album" />
      <span className="c-action album-note">{node.note}</span>
    </div>
  );
}
