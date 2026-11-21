import { useState } from 'react';
import { api } from '../../../core/api/api';
import { useWatch } from '../../../core/api/useWatch';
import { notify } from '../../../core/ui/notify';
import { confirm } from '../../../shared/dialog/confirm';
import { DialogActions, DialogContent, DialogTitle } from '../../../shared/dialog/DialogParts';
import { formatWhen } from '../../../shared/format';
import { TRACK_USERS, UNLINK_USER } from '../library-api';

const ORIGINS = {
  download: 'téléchargé',
  adopted: 'déjà présent',
  restore: 'restauré',
  dedup: 'doublon regroupé',
  admin: 'ajouté',
};

/**
 * Administrateur : qui a ce titre dans sa bibliothèque. Retirer un utilisateur ne touche pas
 * au fichier (le titre disparaît seulement de sa bibliothèque).
 */
export function TrackUsersDialog({ track, close }) {
  const watched = useWatch(TRACK_USERS, { path: track.path });
  const users = watched.data?.trackUsers ?? null;
  const [busy, setBusy] = useState(false);

  const unlink = async user => {
    const ok = await confirm({
      title: 'Retirer de sa bibliothèque',
      message: `Retirer « ${track.title || track.path} » de la bibliothèque de ${user.name} ?\nLe fichier n'est pas touché.`,
      confirm: 'Retirer',
    });
    if (!ok) return;
    setBusy(true);
    const r = await api.mutate(
      UNLINK_USER,
      { userId: user.id, paths: [track.path] },
      { label: 'Retirer un titre', queueable: false, refetch: ['TrackUsers', 'LibraryTracks', 'UserCounts'] },
    );
    setBusy(false);
    if (r.error) notify.error(r.error);
  };

  return (
    <>
      <DialogTitle>Utilisateurs du titre</DialogTitle>
      <DialogContent>
        <p className="dk-help">
          <strong>{track.title || track.path}</strong>
          {track.artist ? ` — ${track.artist}` : ''}
        </p>
        {watched.error && !users && <p className="dk-error-text">{watched.error}</p>}
        {users === null && !watched.error && <p className="dk-faint">Lecture…</p>}
        {users?.length === 0 && (
          <p className="dk-faint">
            Personne n'a ce titre dans sa bibliothèque : le supprimer le mettra à la corbeille.
          </p>
        )}
        {!!users?.length && (
          <ul className="track-users">
            {users.map(u => (
              <li key={u.id}>
                <strong>{u.name}</strong>{' '}
                <span className="dk-faint">
                  {ORIGINS[u.origin] ?? u.origin ?? ''} {formatWhen(u.addedAt)}
                </span>{' '}
                <button className="control-btn small" disabled={busy} onClick={() => void unlink(u)}>
                  Retirer
                </button>
              </li>
            ))}
          </ul>
        )}
      </DialogContent>
      <DialogActions>
        <button className="control-btn" onClick={() => close()}>
          Fermer
        </button>
      </DialogActions>
    </>
  );
}
