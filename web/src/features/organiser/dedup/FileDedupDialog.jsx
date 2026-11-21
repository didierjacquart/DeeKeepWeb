import { Fragment, useState } from 'react';
import { usePermissions } from '../../../core/auth/usePermissions';
import { notify } from '../../../core/ui/notify';
import { confirm } from '../../../shared/dialog/confirm';
import { DialogActions, DialogContent, DialogTitle } from '../../../shared/dialog/DialogParts';
import { toggled } from '../organize-tree';
import { deleteSessionFiles } from './dedup-api';
import { allPaths, defaultChecked, fileDeleteMessage } from './file-dedup';
import { FileDedupRow } from './FileDedupRow';
import './file-dedup-dialog.scss';

/**
 * Écran « Fichiers en double » (fichier/fichier) : un groupe = des fichiers rapprochés par leur nom ou leurs
 * tags. L'exemplaire gardé par défaut (★) est choisi par le serveur (règle unique : qualité, puis rangement) ;
 * tous les autres sont cochés. La suppression va à la corbeille de la bibliothèque.
 */
export function FileDedupDialog({ result: data, sessionId, close }) {
  const [checked, setChecked] = useState(() => defaultChecked(data));
  const [busy, setBusy] = useState(false);
  const { canDelete } = usePermissions();

  const toggle = path => setChecked(s => toggled(s, path));
  const setAll = on => setChecked(on ? allPaths(data) : new Set());

  const remove = async () => {
    const paths = [...checked];
    if (paths.length === 0) {
      notify.info('Cochez au moins un fichier à supprimer.');
      return;
    }
    const ok = await confirm({
      title: 'Confirmer la suppression',
      message: `Supprimer ${paths.length} fichier(s) du disque ?\nIls sont placés dans la corbeille de la bibliothèque (.deekeep-trash).`,
      confirm: 'Supprimer',
      cancel: 'Non',
      danger: true,
    });
    if (!ok) return;
    setBusy(true);
    const r = await deleteSessionFiles(paths, sessionId);
    setBusy(false);
    const result = r.data?.deleteOrganizeFiles;
    if (!result) {
      notify.error(r.error ?? 'Suppression impossible.');
      return;
    }
    const message = fileDeleteMessage(result);
    if (result.errors.length) notify.error(message);
    else notify.success(message);
    close(result.deleted);
  };

  return (
    <div className="dk-file-dedup-dialog">
      <DialogTitle>Fichiers en double</DialogTitle>
      <DialogContent className="content">
        <p className="sub">
          {data.fileCount} fichiers dans {data.groups.length} groupe(s). Cochez ceux à supprimer. Par défaut, chaque
          groupe conserve (décoché) le meilleur exemplaire : le meilleur débit, puis le fichier le plus complet, au
          format cible ({data.targetFormat}), rangé dans le dossier de l'artiste, hors « Divers », à l'emplacement le
          moins profond.
        </p>
        <div className="dk-table list">
          <div className="dk-thead row dk-hide-mobile">
            <span>Suppr.</span>
            <span>Groupe</span>
            <span>Titre</span>
            <span>Artiste</span>
            <span>Format / débit</span>
            <span>Fichier</span>
          </div>
          {data.groups.map(g => (
            <Fragment key={g.number}>
              {g.files.map(f => (
                <FileDedupRow
                  key={f.path}
                  file={f}
                  group={g.number}
                  checked={checked.has(f.dbPath)}
                  onToggle={() => toggle(f.dbPath)}
                />
              ))}
            </Fragment>
          ))}
        </div>
      </DialogContent>
      <DialogActions>
        <button className="control-btn" onClick={() => setAll(true)}>
          Tout cocher
        </button>
        <button className="control-btn" onClick={() => setAll(false)}>
          Tout décocher
        </button>
        <span className="dk-spacer" />
        <button className="control-btn" onClick={() => close(undefined)} disabled={busy}>
          Annuler
        </button>
        <button
          className="danger-btn"
          onClick={() => void remove()}
          disabled={busy || checked.size === 0 || !canDelete}
          data-tip={canDelete ? undefined : "Vous n'avez pas le droit de supprimer"}
        >
          Supprimer les fichiers cochés
        </button>
      </DialogActions>
    </div>
  );
}
