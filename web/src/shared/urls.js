/** URL d'image passée par le cache du serveur (fonctionne hors ligne via le service worker). */
export function imageUrl(url) {
  if (!url) return null;
  if (url.startsWith('/')) return url;
  return `/img?u=${encodeURIComponent(url)}`;
}

/** Flux audio d'un fichier de la bibliothèque (chemin tel que stocké en base). */
export function mediaUrl(dbPath) {
  return `/media/track?path=${encodeURIComponent(dbPath)}`;
}

/** Flux d'un titre lu depuis l'archive (fichier absent du disque). */
export function archiveMediaUrl(dbPath) {
  return `/archive-media/track?path=${encodeURIComponent(dbPath)}`;
}

/** Jaquette stockée en base (table covers). */
export function coverUrl(sha1) {
  return sha1 ? `/covers/${sha1}` : null;
}

/** Extrait Deezer de 30 s, relayé par le serveur. */
export function previewUrl(deezerId) {
  return `/preview/${deezerId}`;
}
