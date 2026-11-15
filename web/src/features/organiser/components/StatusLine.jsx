/** Ligne d'état de la page et filtre « Masquer ce qui est déjà en place ». */
export function StatusLine({ text, hideInPlace, onToggleHide }) {
  return (
    <div className="status-line">
      <span className="status" role="status">
        {text}
      </span>
      <span className="dk-spacer" />
      <label className="dk-check">
        <input type="checkbox" className="dk-check" checked={hideInPlace} onChange={onToggleHide} /> Masquer ce qui est
        déjà en place
      </label>
    </div>
  );
}
