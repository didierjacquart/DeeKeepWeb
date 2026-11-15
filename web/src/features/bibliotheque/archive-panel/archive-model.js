import { isActive } from '../../../core/jobs/jobs';
import { formatBytes } from '../../../shared/format';

/** Types de tâches de l'archive (StorageJobTypes côté serveur). */
export const ARCHIVE_JOB_TYPES = ['archive.upload', 'archive.restore', 'archive.check', 'archive.usage'];
/** Les deux sens de la copie : ils ne tournent pas ensemble et partagent la progression. */
export const ARCHIVE_WORK_TYPES = ['archive.upload', 'archive.restore'];

/** Texte de l'original quand aucune archive n'est enregistrée (_unconfigured_text). */
export const UNCONFIGURED_TEXT =
  'Aucune archive configurée. Ouvrez le volet « Archive » et indiquez le fichier où ranger les titres, puis « Enregistrer ».';

const UNCONFIGURED_TIP =
  'Aucune archive configurée : ouvrez le volet « Archive » et indiquez le fichier où ranger les titres.';

/** Archive configurée (sélecteur pour useStore). */
export const selectConfigured = s => s.config?.configured ?? false;

const human = n => formatBytes(n ?? 0);
export const baseName = p => p.split(/[\\/]/).pop() ?? p;

export function archiveTip(configured) {
  return configured
    ? "Copier les titres cochés dans l'archive, tels quels — aucun réencodage, rien n'est perdu"
    : UNCONFIGURED_TIP;
}

export function restoreTip(configured) {
  return configured
    ? "Ressortir de l'archive les titres qui ne sont plus sur le disque. Rien n'est écrasé : seuls les fichiers absents sont écrits."
    : UNCONFIGURED_TIP;
}

/** Cochés ET affichés, dans l'ordre d'affichage (_selected_entries). */
export function chosenPaths(selected, visible) {
  const checked = new Set(selected);
  return visible.filter(p => checked.has(p));
}

/** Message de l'original quand rien n'est à restaurer parmi les titres retenus. */
export function nothingToRestore(plan) {
  return plan.restorableElsewhere > 0
    ? `Aucun des titres affichés n'est à restaurer, mais ${plan.restorableElsewhere} le sont ailleurs dans la bibliothèque. ` +
        'Cochez « Titres absents du disque », ou videz la recherche, pour les faire apparaître.'
    : "Rien à restaurer : tous les titres que l'archive détient sont déjà sur le disque.";
}

/**
 * Jauge de place sous le chemin de l'archive : texte, alerte, barre (pourcentages
 * occupé / à copier du volume) et détail des compteurs. `null` tant que rien n'est mesuré.
 */
export function archiveGauge(u, root) {
  if (!u) return null;
  if (!root.trim()) {
    return {
      text: `Indiquez le fichier d'archive : les ${u.tracksOnDisk} titres présents sur le disque y entreraient tels quels, soit ${human(u.bytesOnDisk)}.`,
      warn: true,
      bar: null,
      detail: '',
    };
  }
  const free = u.diskFreeBytes ?? 0;
  // Ce qui reste à copier, et non tout ce qui est présent : une archive déjà
  // constituée ne demande plus que la place des titres neufs ou changés.
  const fits = free > u.bytesToArchive * 1.1;
  const parts = [
    `Les ${u.tracksOnDisk} titres présents sur le disque entreraient tels quels dans l'archive, soit ${human(u.bytesOnDisk)} — pour ${human(free)} de libre sur son volume.`,
    fits ? 'Tous reviendraient octet pour octet.' : 'La place ne suffit pas.',
  ];
  if (u.sameVolume) {
    parts.push(
      "Attention : l'archive est sur le même volume que la bibliothèque — elle ne survivrait pas à une panne de ce disque.",
    );
  }
  if (u.error) parts.push(u.error);
  const total = u.archiveFileBytes + free;
  const bar =
    total > 0
      ? {
          used: (100 * u.archiveFileBytes) / total,
          todo: Math.min(100 - (100 * u.archiveFileBytes) / total, (100 * u.bytesToArchive) / total),
        }
      : null;
  const detail =
    `Archive : ${human(u.archiveFileBytes)} (${u.objectCount} objets) · ${u.tracksArchived} titres archivés · ` +
    `${u.tracksStale} à renvoyer · ${u.tracksNotArchived} non archivés — reste ${human(u.bytesToArchive)} à archiver.`;
  return { text: parts.join(' '), warn: !fits || u.sameVolume, bar, detail };
}

/**
 * Ligne d'état d'une tâche de copie : son message tant qu'elle tourne, puis son
 * bilan, une fois, à la transition (ou au retour sur la page). `null` = ligne inchangée.
 * @param {object} job tâche la plus récente
 * @param {string | undefined} previous dernier statut vu pour cette tâche
 */
export function workLine(job, previous) {
  if (isActive(job)) return job.message ? { text: job.message, error: false } : null;
  if (previous === job.status) return null;
  const r = job.result ?? {};
  if (job.status === 'SUCCEEDED') {
    const done = (r.sent ?? 0) + (r.restored ?? 0);
    return { text: job.message ?? 'Terminé.', error: (r.failed ?? 0) > 0 && done === 0 };
  }
  if (job.status === 'CANCELLED') {
    return {
      text: job.message && job.message !== 'Arrêtée' ? `Arrêtée — ${job.message}` : 'Arrêtée.',
      error: false,
    };
  }
  return { text: job.error ?? 'Échec.', error: true };
}

/** Bilan d'une tâche du volet (mesure, vérification). */
export function panelJobLine(job) {
  if (job.status === 'SUCCEEDED') return { text: job.message ?? 'Terminé.', error: false };
  if (job.status === 'CANCELLED') return { text: 'Arrêtée.', error: false };
  return { text: job.error ?? job.message ?? 'Échec.', error: true };
}

/** Dernier échec d'une copie, élément par élément (données de jobItem) ; `null` si l'élément a réussi. */
export function failureLine(item) {
  if (item?.status !== 'failed') return null;
  return `${baseName(item.data?.path ?? item.itemKey)} — ${item.message ?? ''}`;
}
