/**
 * Libellés et états dérivés de la page Organiser (ligne d'état, progression, liste vide, messages).
 */

/** Au-delà de ce nombre de fichiers, la recherche des tags est annoncée longue. */
export const TAGS_LOOKUP_WARN = 50;
const SECONDS_PER_LOOKUP = 1.2;

export const isTerminal = status => status === 'SUCCEEDED' || status === 'CANCELLED' || status === 'FAILED';
export const isRunning = job => !!job && (job.status === 'QUEUED' || job.status === 'RUNNING');

/** Ligne d'état : le rangement, sinon l'analyse en cours, sinon le dernier message ou celui du plan. */
export function statusText({ applying, applyJob, scanning, job, session, message, plan }) {
  if (applying) return applyJob?.message ?? 'Rangement…';
  if (scanning) return job?.message ?? session?.message ?? 'Analyse en cours…';
  return message || plan?.statusText || '';
}

/** Avancement de la tâche suivie (null = indéterminé). */
export function progressOf(job) {
  return job ? (job.total > 0 ? job.progress : null) : null;
}

export function emptyText(loaded, scanning) {
  if (!loaded) return 'Chargement…';
  if (scanning) return 'Analyse en cours…';
  return 'Aucune analyse : choisissez un dossier et lancez « Analyser ».';
}

/** Compte rendu d'un rangement terminé. */
export function applyResultText(job) {
  const summary = job.result?.summary;
  if (job.status === 'FAILED') return `Le rangement a échoué : ${job.error ?? ''}`;
  return (job.status === 'CANCELLED' ? 'Rangement arrêté entre deux fichiers. ' : '') + (summary ?? '');
}

export const NOTHING_TO_LOOKUP =
  "Aucun fichier coché ne manque de tag : ils portent tous leur titre, leur artiste et leur album, ainsi que leur artiste d'album, leur genre, leur année et leur numéro de piste.";

/** Avertissement avant une longue recherche de tags. */
export function longLookupMessage(count) {
  const minutes = Math.max(1, Math.round((count * SECONDS_PER_LOOKUP) / 60));
  return `${count} fichiers cochés n'ont pas tous leurs tags.\n\nLe catalogue est interrogé fichier par fichier : comptez de l'ordre de ${minutes} minute(s). La recherche s'interrompt à tout moment — les correspondances déjà trouvées restent écrivables, les fichiers non encore interrogés sont laissés tels quels et un passage suivant les reprendra. Rien n'est écrit sans validation.\n\nLancer la recherche ?`;
}
