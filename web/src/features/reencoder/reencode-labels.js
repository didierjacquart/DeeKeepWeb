import { imageUrl, mediaUrl } from '../../shared/urls';

/** « MP3 · 320 kbps », le format seul si le débit est inconnu, « ? » sans format. */
export function formatText(i) {
  if (!i.format) return '?';
  return i.bitrate > 0 ? `${i.format} · ${i.bitrate} kbps` : i.format;
}

/** Jaquette de la correspondance Deezer (null sans correspondance). */
export function coverOf(i) {
  return imageUrl(i.match?.coverSmall ?? i.match?.coverBig);
}

/** Colonne « Correspondance » : texte, classe de couleur, infobulle. */
export function matchOf(i) {
  switch (i.matchState) {
    case 'found':
      return { text: '✓ Trouvé', cls: 'ok', tip: i.match ? `${i.match.artist} - ${i.match.title}` : '' };
    case 'skipped':
      return { text: '—', cls: 'muted', tip: 'Correspondance non recherchée (trop de fichiers)' };
    case 'none':
      return { text: '✗ Introuvable', cls: 'err', tip: '' };
    default:
      return { text: '⏳', cls: 'muted', tip: '' };
  }
}

/** Colonne « Statut », d'après la tâche de conversion de la ligne (`job` nul si aucune). */
export function statusOf(i, job) {
  if (i.converted || job?.status === 'SUCCEEDED') return { text: 'Réencodé ✓', cls: 'ok', tip: '' };
  switch (job?.status) {
    case 'QUEUED':
      return { text: 'En attente...', cls: 'muted', tip: '' };
    case 'RUNNING':
      return {
        text: `${job.message || 'Initialisation...'} — ${Math.round(job.progress * 100)}%`,
        cls: 'info',
        tip: '',
      };
    case 'FAILED':
      return { text: 'Erreur', cls: 'err', tip: job.error ?? '' };
    case 'CANCELLED':
      return { text: 'Annulé', cls: 'err', tip: '' };
    default:
      return { text: 'En attente', cls: 'muted', tip: '' };
  }
}

/** Piste du lecteur pour un fichier de la liste. */
export function playerTrackOf(x) {
  return {
    id: x.dbPath,
    title: x.title || x.path,
    artist: x.artist,
    album: x.album,
    src: mediaUrl(x.dbPath),
    cover: coverOf(x),
    context: `${formatText(x)}  ·  ${x.path}`,
  };
}

/** Message de la liste vide : l'avancement de l'analyse en cours, sinon la raison. */
export function emptyText(scanText, total) {
  if (scanText !== null) return scanText;
  return total
    ? 'Tous les fichiers sont déjà au format cible.'
    : 'Aucun fichier. Ajoutez des fichiers ou un dossier entier (sous-dossiers inclus) pour commencer.';
}
