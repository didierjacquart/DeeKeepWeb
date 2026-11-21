const text = v => (v == null || v === '' ? null : String(v));
const num = v => (v == null || v === '' || Number.isNaN(Number(v)) ? null : Number(v));

/** Durée en secondes ; accepte aussi « m:ss » ou « h:mm:ss ». */
function seconds(value) {
  if (typeof value === 'string' && value.includes(':')) {
    return value
      .split(':')
      .reverse()
      .reduce((acc, p, i) => acc + Number(p) * 60 ** i, 0);
  }
  return num(value) ?? 0;
}

/** Piste de la recherche (ou ligne existante) → corps JSON de l'API ; tolère les variantes de noms de champs. */
export function toInput(t) {
  return {
    id: num(t['id']) ?? 0,
    title: text(t['title']) ?? '',
    artist: text(t['artist']) ?? '',
    album: text(t['album']),
    duration: seconds(t['duration']),
    artistId: num(t['artistId']),
    albumId: num(t['albumId']),
    previewUrl: text(t['previewUrl'] ?? t['preview']),
    coverSmall: text(t['coverSmall'] ?? t['coverUrl'] ?? t['cover']),
    coverBig: text(t['coverBig']),
    link: text(t['link']),
    trackPosition: num(t['trackPosition']),
    diskNumber: num(t['diskNumber']),
  };
}
