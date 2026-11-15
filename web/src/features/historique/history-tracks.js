import { coverUrl, mediaUrl } from '../../shared/urls';

/** Piste du lecteur pour une entrée de l'historique des téléchargements. */
export function playerTrackOf(e) {
  return {
    id: e.path,
    title: e.title || e.path.split(/[\\/]/).pop() || e.path,
    artist: e.artist,
    album: e.album,
    src: mediaUrl(e.path),
    cover: coverUrl(e.coverSha1),
  };
}

const text = v => (v == null || v === '' ? null : String(v));
const num = v => (v == null || v === '' || Number.isNaN(Number(v)) ? null : Number(v));

/** Durée en secondes ; accepte aussi « m:ss » ou « h:mm:ss ». */
function durationOf(v) {
  if (typeof v === 'string' && v.includes(':'))
    return v
      .split(':')
      .reverse()
      .reduce((acc, p, i) => acc + Number(p) * 60 ** i, 0);
  return num(v) ?? 0;
}

/**
 * Pistes choisies dans la fenêtre de recherche → entrées `DownloadTrackInput` de la
 * liste d'attente (les noms de champs varient selon l'origine de la piste ; sans
 * identifiant Deezer, la piste est écartée).
 */
export function toPendingTracks(tracks) {
  return tracks
    .map(t => ({
      id: num(t.id) ?? 0,
      title: text(t.title) ?? '',
      artist: text(t.artist) ?? '',
      album: text(t.album),
      duration: durationOf(t.duration),
      artistId: num(t.artistId),
      albumId: num(t.albumId),
      previewUrl: text(t.previewUrl ?? t.preview),
      coverSmall: text(t.coverSmall ?? t.coverUrl ?? t.cover),
      coverBig: text(t.coverBig),
      link: text(t.link),
      trackPosition: num(t.trackPosition),
      diskNumber: num(t.diskNumber),
    }))
    .filter(t => t.id);
}
