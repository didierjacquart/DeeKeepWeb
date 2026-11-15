import { useMemo, useState } from 'react';
import { notify } from '../../../core/ui/notify';
import { confirm } from '../../../shared/dialog/confirm';
import { DialogActions, DialogContent, DialogTitle } from '../../../shared/dialog/DialogParts';
import { toggled } from '../organize-tree';
import { deleteLibraryFiles } from './dedup-api';
import { buildTrackDedupGroups, deleteLabel, trackDeleteMessage } from './track-dedup-groups';
import { TrackDedupGroup } from './TrackDedupGroup';
import './track-dedup-dialog.scss';

/** Écran « Doublons de la bibliothèque » (Dédoublonner… de la fenêtre de recherche). */
export function TrackDedupDialog({ items, close }) {
  const built = useMemo(() => buildTrackDedupGroups(items), [items]);
  const groups = built.groups;
  const fileCount = groups.reduce((n, g) => n + g.files.length, 0);
  const [checked, setChecked] = useState(() => new Set(built.checked));
  const [busy, setBusy] = useState(false);
  const count = checked.size;

  const toggle = path => setChecked(s => toggled(s, path));
  const setAll = on => setChecked(on ? new Set(groups.flatMap(g => g.files.map(f => f.path))) : new Set());

  const confirmDelete = async () => {
    const paths = [...checked];
    if (paths.length === 0) {
      notify.info('Cochez au moins un fichier à supprimer.');
      return;
    }
    const ok = await confirm({
      title: 'Confirmer la suppression',
      message: `Supprimer ${paths.length} fichier(s) du disque ?\nIls sont placés dans la corbeille de la bibliothèque.`,
      confirm: 'Supprimer',
      cancel: 'Non',
      danger: true,
    });
    if (!ok) return;
    setBusy(true);
    const r = await deleteLibraryFiles(paths);
    setBusy(false);
    if (r.error || !r.data) {
      notify.error(r.error ?? 'Suppression impossible.');
      return;
    }
    const result = r.data.deleteOrganizeFiles;
    const message = trackDeleteMessage(result);
    if (result.errors.length) notify.error(message);
    else notify.success(message);
    close(result.deleted);
  };

  return (
    <div className="dk-track-dedup-dialog">
      <DialogTitle>Doublons de la bibliothèque</DialogTitle>
      <DialogContent className="content">
        <p className="sub">
          {groups.length} piste(s) de la recherche répondent à {fileCount} fichier(s) déjà sur le disque. Cochez les
          fichiers à supprimer : la piste redevient alors sélectionnable dans la recherche, prête à être extraite de
          nouveau. Les pistes dont un seul fichier répond ne sont pas cochées d'avance — une ressemblance n'est pas une
          preuve.
        </p>
        <div className="dk-table tree">
          <div className="dk-thead head dk-hide-mobile">
            <span>Piste Deezer / fichiers du disque</span>
            <span>Qualité</span>
            <span>Motif / emplacement</span>
          </div>
          {groups.map(g => (
            <TrackDedupGroup key={g.key} group={g} checked={checked} onToggle={toggle} />
          ))}
          {groups.length === 0 && (
            <div className="dk-empty">Aucun fichier du disque ne répond aux pistes affichées.</div>
          )}
        </div>
      </DialogContent>
      <DialogActions>
        <button className="control-btn" onClick={() => setAll(true)} disabled={busy}>
          Tout cocher
        </button>
        <button className="control-btn" onClick={() => setAll(false)} disabled={busy}>
          Tout décocher
        </button>
        <span className="dk-spacer" />
        <button className="control-btn" onClick={() => close(undefined)} disabled={busy}>
          Fermer
        </button>
        <button className="danger-btn" onClick={() => void confirmDelete()} disabled={count === 0 || busy}>
          {deleteLabel(count)}
        </button>
      </DialogActions>
    </div>
  );
}
