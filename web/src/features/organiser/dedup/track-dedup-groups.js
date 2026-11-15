import { baseName, bestCopyIndex, dirName, fileQuality, pathKey } from '../organize-rules';

/**
 * Écran « Doublons de la bibliothèque » : calculs purs.
 * Une piste de la recherche (`key`, `title`, `artist`, `album`) et les fichiers du disque qui lui répondent
 * (`path`, `ext`, `bitrate`, `fileSize`, `reason` : verdict de l'index de présence).
 */

/**
 * Groupes de l'écran : un par piste, chaque fichier montré une seule fois
 * (sous la première piste qui le cite — deux cases ne décident pas du sort
 * d'un même fichier). Pré-cochage : rien si un seul fichier répond (une
 * ressemblance n'est pas une preuve) ; sinon tous sauf le meilleur exemplaire.
 */
export function buildTrackDedupGroups(items) {
  const seen = new Set();
  const groups = [];
  const checked = new Set();
  for (const item of items ?? []) {
    const files = (item.files ?? []).filter(f => {
      const k = pathKey(f.path);
      if (!f.path || seen.has(k)) return false;
      seen.add(k);
      return true;
    });
    if (files.length === 0) continue;
    const keep = files.length > 1 ? bestCopyIndex(files.map(f => ({ ...f, artist: item.artist }))) : -1;
    const rows = files.map((f, i) => ({
      path: f.path,
      name: baseName(f.path),
      folder: dirName(f.path),
      quality: fileQuality(f.path, f.ext, f.bitrate, f.fileSize),
      qualityTip: i === keep ? 'Meilleur exemplaire du groupe : conservé par défaut' : '',
      reason: f.reason ?? '',
      keep: i === keep,
    }));
    if (keep >= 0) rows.forEach((r, i) => i !== keep && checked.add(r.path));
    const reasons = [...new Set(files.map(f => f.reason).filter(r => !!r))];
    groups.push({
      key: item.key,
      title: item.title ?? '',
      subtitle: [item.artist, item.album].filter(p => !!p).join(' — '),
      reason: reasons.join(' · '),
      files: rows,
    });
  }
  return { groups, checked };
}

/** Libellé du bouton de suppression. */
export const deleteLabel = count =>
  count ? `Supprimer les ${count} fichier(s) cochés` : 'Supprimer les fichiers cochés';

/** Compte rendu d'une suppression (les dix premières erreurs, puis leur nombre). */
export function trackDeleteMessage(result) {
  let message = `${result.deleted.length} fichier(s) supprimé(s).`;
  if (result.errors.length) {
    message += '\n\nErreurs :\n' + result.errors.slice(0, 10).join('\n');
    if (result.errors.length > 10) message += `\n… et ${result.errors.length - 10} autre(s).`;
  }
  return message;
}
