/** Calculs purs de la fenêtre de recherche : présence, libellés, navigation. */

export const KIND_OPTIONS = [
  { value: 'ALL', label: 'Tout / Titre' },
  { value: 'ARTIST', label: 'Artiste' },
  { value: 'ALBUM', label: 'Album' },
  { value: 'PLAYLIST', label: 'Playlist' },
];

/** Déjà sur le disque (✓) ou soupçonné d'y être (≈). */
export function isDuplicate(t) {
  return t.presence.state === 'PRESENT' || t.presence.state === 'POSSIBLE';
}

/** L'index de présence n'a pas encore répondu pour au moins une piste. */
export function hasPendingPresence(tracks) {
  return tracks.some(t => t.presence.state === 'PENDING');
}

/** Infobulle de la pastille : raison, puis les cinq premiers fichiers en cause. */
export function presenceTip(t) {
  const p = t.presence;
  const lines = [p.reason ?? ''];
  for (const f of p.files.slice(0, 5)) lines.push(`• ${f.path}`);
  if (p.files.length > 5) lines.push(`… et ${p.files.length - 5} autre(s)`);
  return lines.filter(Boolean).join('\n');
}

export function checkTip(t) {
  return t.presence.state === 'PRESENT' ? presenceTip(t) : 'Ajouter ce titre à la file de téléchargement';
}

/** Jauge de popularité : 7 barres, toutes allumées à partir d'un rang Deezer d'un million. */
export function popularityBars(t) {
  const lit = Math.round(Math.min((t.rank ?? 0) / 1_000_000, 1) * 7);
  return Array.from({ length: 7 }, (_, i) => i < lit);
}

/** Nombre de pistes déjà présentes et de doublons possibles. */
export function presenceCounts(tracks) {
  let present = 0;
  let possible = 0;
  for (const t of tracks) {
    if (t.presence.state === 'PRESENT') present++;
    else if (t.presence.state === 'POSSIBLE') possible++;
  }
  return { present, possible };
}

/** Pistes et fichiers du disque proposés à l'écran de dédoublonnage. */
export function dedupItems(tracks) {
  return tracks.map(t => ({
    key: String(t.id),
    title: t.title,
    artist: t.artist,
    album: t.album,
    files: t.presence.files.map(f => ({
      path: f.path,
      reason: f.reason,
      bitrate: f.bitrate,
      fileSize: f.fileSize,
      ext: f.ext,
    })),
  }));
}

/** Genre d'exploration d'une entité affichée (artiste, playlist, sinon album). */
export function entityKind(entity) {
  return entity.kind === 'ARTIST' ? 'ARTIST' : entity.kind === 'PLAYLIST' ? 'PLAYLIST' : 'ALBUM';
}

export function entityLabel(e) {
  return e.kind === 'ARTIST' ? 'Voir le top 50' : "Ouvrir l'album";
}

/** Fil d'Ariane d'une exploration. */
export function crumbFor(kind, name) {
  return kind === 'ARTIST' ? `Artiste : ${name} — top 50` : kind === 'ALBUM' ? `Album : ${name}` : `Playlist : ${name}`;
}

/** Une recherche qui trouve des entités les montre ; une exploration sans pistes aussi. */
export function isEntitiesView(data, req) {
  return (req.type === 'search' && data.entities.length > 0) || (data.tracks.length === 0 && data.entities.length > 0);
}

/** Un résultat de pistes dont la présence doit encore être recalculée. */
export function needsPresenceRetry(data, req) {
  return !data.error && !isEntitiesView(data, req) && hasPendingPresence(data.tracks);
}

export function searchError(text) {
  return { title: 'Erreur de connexion', text: `Erreur lors de la recherche : ${text}`, error: true };
}

export function noResultMessage(req) {
  const wasEntity = req.type === 'search' && (req.kind === 'ARTIST' || req.kind === 'ALBUM');
  return {
    title: 'Aucun résultat',
    text: wasEntity ? 'Aucun résultat trouvé.' : 'Aucun morceau trouvé pour cette recherche.',
    error: false,
  };
}
