/** Compte rendu du dernier rangement. */
export function ResultCard({ text, onClose }) {
  return (
    <div className="dk-card result">
      <pre>{text}</pre>
      <button className="control-btn" onClick={onClose}>
        Fermer
      </button>
    </div>
  );
}
