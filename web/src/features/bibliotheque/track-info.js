import { formatBytes } from '../../shared/format';
import { archiveMediaUrl, coverUrl, mediaUrl } from '../../shared/urls';

/** Écoutable : présent sur le disque, ou copie d'archive à jour. */
export function playable(t) {
  return t.onDisk || t.archiveState === 'ok';
}

export function playTip(t) {
  if (t.onDisk) return 'Écouter ce titre';
  return t.archiveState === 'ok'
    ? "Écouter la copie de l'archive (fichier absent du disque)"
    : "Fichier absent du disque, et pas de copie d'archive";
}

export function cloudTip(t) {
  if (t.archiveState === 'stale') return "Une copie existe dans l'archive, mais le fichier local a changé depuis.";
  return t.archiveState === 'none' ? "Ce titre n'est pas dans l'archive." : "Copie conservée dans l'archive.";
}

/** Texte de la colonne NUAGE. */
export function cloudText(t) {
  if (t.archiveState === 'ok') return `✓ ${formatBytes(t.archiveBytes)}`;
  return t.archiveState === 'stale' ? 'à renvoyer' : '—';
}

/** Titre pour le lecteur : le fichier du disque, sinon la copie de l'archive. */
export function asPlayerTrack(t) {
  return {
    id: t.path,
    title: t.title || t.path.split(/[\\/]/).pop() || t.path,
    artist: t.artist,
    album: t.album,
    src: t.onDisk ? mediaUrl(t.path) : archiveMediaUrl(t.path),
    cover: coverUrl(t.coverSha1),
    duration: t.duration,
    context: [t.format, t.year, t.genre].filter(Boolean).join(' · ') || null,
  };
}
