import { useMemo, useState } from 'react';
import { api } from '../../../core/api/api';
import { useWatch } from '../../../core/api/useWatch';
import { notify } from '../../../core/ui/notify';
import { confirm } from '../../../shared/dialog/confirm';
import { DialogActions, DialogContent, DialogTitle } from '../../../shared/dialog/DialogParts';
import { formatBytes, formatWhen } from '../../../shared/format';
import { TRASH, TRASH_PURGE, TRASH_RESTORE } from './trash-api';
import { daysLeft } from './trash-model';
import './trash-dialog.scss';

/**
 * Corbeille de la bibliothèque : fichiers supprimés depuis l'application, récupérables
 * le temps réglé dans les Paramètres (indéfiniment si la purge automatique est désactivée). « Restaurer » les remet à leur place (et sur le disque USB à la prochaine
 * synchronisation) ; « Supprimer définitivement » les efface sans retour.
 */
export function TrashDialog({ close }) {
  const watched = useWatch(TRASH);
  const entries = watched.data?.trash ?? null;
  const error = watched.error && !watched.data ? watched.error : null;
  const [checked, setChecked] = useState(() => new Set());
  const [busy, setBusy] = useState(false);

  const totalBytes = useMemo(() => (entries ?? []).reduce((s, e) => s + e.size, 0), [entries]);
  const toggle = id =>
    setChecked(s => {
      const next = new Set(s);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  const allChecked = !!entries?.length && checked.size === entries.length;
  const toggleAll = () => setChecked(allChecked ? new Set() : new Set((entries ?? []).map(e => e.id)));

  const run = async (op, ids, label) => {
    setBusy(true);
    const r = await api.mutate(op, { ids }, { label, queueable: false });
    setBusy(false);
    const result = r.data && Object.values(r.data)[0];
    if (r.error) notify.error(r.error);
    else if (result?.errors?.length) notify.error(result.errors.join(' ; '));
    if (result?.done) notify.success(`${label} : ${result.done} fichier(s).`);
    setChecked(new Set());
    api.refetch(['Trash']);
  };

  const restore = () => void run(TRASH_RESTORE, [...checked], 'Restauration');
  const purge = async ids => {
    const count = ids.length || entries?.length || 0;
    const ok = await confirm({
      title: 'Supprimer définitivement',
      message: `Effacer définitivement ${count} fichier(s) ?\nIls ne pourront plus être récupérés.`,
      confirm: 'Effacer',
      danger: true,
    });
    if (ok) await run(TRASH_PURGE, ids, 'Effacement définitif');
  };

  return (
    <>
      <DialogTitle>Corbeille</DialogTitle>
      <DialogContent className="trash-dialog">
        <p className="dk-help">
          Titres supprimés depuis l'application : ils restent ici le temps réglé dans les Paramètres, puis sont effacés
          (jamais si la purge automatique est désactivée). Une restauration les remet à leur place dans la bibliothèque,
          et sur le disque USB à la prochaine synchronisation.
        </p>
        {error && <p className="dk-error-text">Corbeille illisible : {error}</p>}
        {entries === null && !error && <p className="dk-faint">Lecture de la corbeille…</p>}
        {entries?.length === 0 && <p className="dk-faint">La corbeille est vide.</p>}
        {!!entries?.length && (
          <div className="dk-table trash-table" role="table">
            <div className="dk-thead trash-row" role="row">
              <label className="dk-check">
                <input type="checkbox" checked={allChecked} onChange={toggleAll} aria-label="Tout cocher" />
              </label>
              <span>Titre</span>
              <span>Dossier</span>
              <span>Supprimé</span>
              <span>Reste</span>
              <span>Taille</span>
            </div>
            {entries.map(e => (
              <div key={e.id} className="dk-row trash-row" role="row">
                <label className="dk-check">
                  <input
                    type="checkbox"
                    checked={checked.has(e.id)}
                    onChange={() => toggle(e.id)}
                    aria-label={e.name}
                  />
                </label>
                <span className="ellipsis" data-tip={e.dbPath}>
                  {e.name}
                </span>
                <span className="ellipsis dk-faint">{e.folder}</span>
                <span>{formatWhen(e.deletedAt)}</span>
                <span>{daysLeft(e.expiresAt)}</span>
                <span>{formatBytes(e.size)}</span>
              </div>
            ))}
          </div>
        )}
      </DialogContent>
      <DialogActions>
        {!!entries?.length && (
          <span className="dk-faint trash-total">
            {entries.length} fichier(s), {formatBytes(totalBytes)}
          </span>
        )}
        <button className="danger-btn" disabled={busy || !entries?.length} onClick={() => void purge([])}>
          Vider la corbeille
        </button>
        <button className="danger-btn" disabled={busy || checked.size === 0} onClick={() => void purge([...checked])}>
          Supprimer définitivement
        </button>
        <button className="primary-btn" disabled={busy || checked.size === 0} onClick={restore}>
          Restaurer ({checked.size})
        </button>
        <button className="control-btn" onClick={() => close()}>
          Fermer
        </button>
      </DialogActions>
    </>
  );
}
