import { useMemo } from 'react';
import { useStore } from '../../../lib/store';
import { offlineAudio } from '../../../core/player/offline-audio';
import { Menu } from '../../../shared/menu/Menu';
import { MenuItem } from '../../../shared/menu/MenuItem';
import { mediaUrl } from '../../../shared/urls';
import { archiveActions } from '../archive-panel/archive-actions';
import { albumOwner, albumTracks, artistTracks, countLabel, groupLabel } from '../listen-groups';
import { copyPath, playGroup, removeFromArchive, togglePin } from '../track-actions';
import { playable } from '../track-info';

/** Album et artiste du titre, avec leurs titres écoutables (calculés à l'ouverture du menu). */
function useGroups(tracks, t) {
  return useMemo(() => {
    if (!t) return { album: null, artist: null };
    const artistName = (t.artist || '').trim() || albumOwner(t);
    return {
      album: t.album ? { name: t.album, tracks: albumTracks(tracks, t) } : null,
      artist: artistName ? { name: artistName, tracks: artistTracks(tracks, artistName) } : null,
    };
  }, [tracks, t]);
}

function GroupItem({ kind, group }) {
  if (!group) return null;
  const what = groupLabel(kind, group.name);
  return (
    <MenuItem disabled={!group.tracks.length} onClick={() => playGroup(group.tracks, what)}>
      Écouter {what} ({countLabel(group.tracks.length)})
    </MenuItem>
  );
}

/**
 * Menu contextuel d'un titre : écoute (du titre, de son album, de son artiste), épinglage
 * hors ligne, archive, chemin.
 * @param {{ menu: { x: number, y: number, track: object } | null, tracks: object[], onClose: () => void,
 *   onPlay: (t: object) => void }} props
 */
export function TrackMenu({ menu, tracks, onClose, onPlay }) {
  const pinned = useStore(offlineAudio, s => s.pinned);
  const t = menu?.track;
  const groups = useGroups(tracks, t);
  const isPinned = !!t && pinned.has(mediaUrl(t.path));

  return (
    <Menu at={menu} onClose={onClose}>
      {t && (
        <>
          <MenuItem disabled={!playable(t)} onClick={() => onPlay(t)}>
            Écouter dans le lecteur
          </MenuItem>
          <GroupItem kind="album" group={groups.album} />
          <GroupItem kind="artist" group={groups.artist} />
          <MenuItem disabled={!t.onDisk} onClick={() => void togglePin(t)}>
            {isPinned ? 'Retirer de l’écoute hors ligne' : 'Épingler pour l’écoute hors ligne'}
          </MenuItem>
          <MenuItem disabled={!t.onDisk} onClick={() => void archiveActions.archive([t.path])}>
            Archiver ce titre
          </MenuItem>
          <MenuItem
            disabled={t.onDisk || t.archiveState === 'none'}
            onClick={() => void archiveActions.restore([t.path])}
          >
            Restaurer ce titre depuis l'archive
          </MenuItem>
          <MenuItem disabled={t.archiveState === 'none'} onClick={() => void removeFromArchive(t)}>
            Retirer de l'archive
          </MenuItem>
          <MenuItem onClick={() => void copyPath(t)}>Copier le chemin</MenuItem>
        </>
      )}
    </Menu>
  );
}
