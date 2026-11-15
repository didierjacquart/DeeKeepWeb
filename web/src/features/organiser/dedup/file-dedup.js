/**
 * Écran « Fichiers en double » (fichier/fichier) : calculs purs.
 * Groupes de doublons : résultat de la tâche organize.duplicates.
 */

/** Format et débit d'un fichier : « MP3 · 320 kbps ». */
export function formatLabel(f) {
  const fmt = f.format || '?';
  return f.bitrate ? `${fmt} · ${f.bitrate} kbps` : fmt;
}

/** Cochés d'office : tous les fichiers sauf l'exemplaire gardé de chaque groupe. */
export function defaultChecked(data) {
  return new Set(data.groups.flatMap(g => g.files.filter(f => !f.keep).map(f => f.dbPath)));
}

export function allPaths(data) {
  return new Set(data.groups.flatMap(g => g.files.map(f => f.dbPath)));
}

/** Compte rendu d'une suppression (les dix premières erreurs). */
export function fileDeleteMessage(result) {
  let message = `${result.deleted.length} fichier(s) supprimé(s).`;
  if (result.errors.length) message += '\n\nErreurs :\n' + result.errors.slice(0, 10).join('\n');
  return message;
}
