import { useStore } from '../../../lib/store';
import { jobs } from '../../../core/jobs/jobs';
import { jobOf } from '../reencode-items';
import { ReencodeRow } from './ReencodeRow';
import { ReencodeTableHead } from './ReencodeTableHead';

/** Tableau des fichiers à réencoder ; `empty` est le message affiché quand aucune ligne n'est visible. */
export function ReencodeTable({ view, empty, onToggle, onListen }) {
  const byId = useStore(jobs, s => s.byId);
  return (
    <div className="dk-table">
      <ReencodeTableHead sortKey={view.sortKey} sortAsc={view.sortAsc} onSort={view.sort} />
      {view.visible.map(i => (
        <ReencodeRow
          key={i.path}
          item={i}
          job={jobOf(i, byId)}
          onToggle={() => onToggle(i)}
          onListen={() => onListen(i)}
        />
      ))}
      {!view.visible.length && <div className="dk-empty">{empty}</div>}
    </div>
  );
}
