import { archiveTip, restoreTip } from '../archive-model';

/** Ligne du volet : dépliage, puis Restaurer, Arrêter et Archiver (action principale, en dernier). */
export function ArchiveBar({ open, onToggle, busy, configured, anyActive, onRestore, onStop, onArchive }) {
  return (
    <div className="bar">
      <button className="control-btn toggle" aria-expanded={open} onClick={onToggle}>
        {open ? '▾ Archive' : '▸ Archive'}
      </button>
      <span className="dk-spacer" />
      <button
        className="control-btn"
        disabled={busy || !configured}
        data-tip={restoreTip(configured)}
        onClick={() => void onRestore()}
      >
        Restaurer
      </button>
      <button className="control-btn" disabled={!anyActive} onClick={() => void onStop()}>
        Arrêter
      </button>
      <button
        className="primary-btn"
        disabled={busy || !configured}
        data-tip={archiveTip(configured)}
        onClick={() => void onArchive()}
      >
        Archiver
      </button>
    </div>
  );
}
