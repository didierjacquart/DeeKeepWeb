/**
 * Revue « Tags manquants » : calculs purs. Une proposition de tags du Catalogue arrive comme événement
 * job.item de la tâche « tags.find » (`proposed`, `missing`, `contradicted`, `score`, `confidence`, `source`, `note`).
 */

const FIELD_LABELS = {
  title: 'titre',
  artist: 'artiste',
  album: 'album',
  albumartist: 'artiste album',
  genre: 'genre',
  date: 'année',
  track: 'piste',
  tracktotal: 'total pistes',
  disc: 'disque',
};
const CORE = new Set(['title', 'artist', 'album']);

export const fieldLabel = f => FIELD_LABELS[f] ?? f;

export const isSure = p =>
  /sure|sûr|certain|high/i.test(p.confidence ?? '') || (!/probable|prob/i.test(p.confidence ?? '') && p.score >= 0.8);

/** Lignes de l'écran : une par fichier soumis, « pending » tant que la recherche ne l'a pas examiné. */
export function buildRows(files, results) {
  return files.map(file => {
    const r = results.get(file.dbPath) ?? results.get(file.path);
    if (!r) return { file, state: 'pending', proposal: null, sure: false, fills: [], corrects: [] };
    const p = r.proposal;
    if (!p || !p.proposed) return { file, state: 'none', proposal: null, sure: false, fills: [], corrects: [] };
    const pair = fields => (fields ?? []).filter(f => p.proposed?.[f]).map(f => [f, p.proposed[f]]);
    return {
      file,
      state: 'found',
      proposal: p,
      sure: isSure(p),
      fills: pair(p.missing),
      corrects: pair(p.contradicted),
    };
  });
}

export function currentTags(f) {
  const parts = [f.titleTagged ? f.title : '', f.artist, f.album].filter(p => !!p);
  return parts.length ? parts.join(' · ') : '(aucun)';
}

export function foundText(r) {
  if (r.state === 'pending') return 'en attente…';
  if (r.state === 'none') return 'aucune correspondance trouvée';
  const p = r.proposal.proposed;
  return (
    [p['title'], p['artist'], p['album']].filter(v => !!v).join(' · ') ||
    [...r.fills, ...r.corrects].map(([f, v]) => `${fieldLabel(f)} = ${v}`).join(' · ')
  );
}

export function foundTip(r) {
  const pr = r.proposal;
  if (!pr) return '';
  const p = pr.proposed ?? {};
  return [
    `Titre : ${p['title'] ?? ''}`,
    `Artiste : ${p['artist'] ?? ''}`,
    `Album : ${p['album'] ?? ''}`,
    `Source : ${pr.source}`,
    pr.note ?? '',
  ]
    .filter(l => !!l)
    .join('\n');
}

export function matchText(r) {
  if (r.state === 'pending') return '';
  if (r.state === 'none')
    return r.file.coreMissing
      ? 'tags, nom de fichier et dossier trop pauvres'
      : 'le catalogue ne reconnaît pas cet enregistrement';
  const p = r.proposal;
  return `${r.sure ? 'sûre' : 'probable'} (${p.score.toFixed(2)})${p.note ? ' — ' + p.note : ''}`;
}

/** Champs comblés ; les champs contredits sont signalés « ⟳ » (ignorés sans « Corriger aussi… »). */
export function fillsText(r, correct) {
  if (r.state !== 'found') return r.state === 'none' ? '—' : '';
  const filled = r.fills.map(([f, v]) => (CORE.has(f) ? fieldLabel(f) : `${fieldLabel(f)} = ${v}`)).join(' · ');
  const fix = r.corrects.map(([f]) => fieldLabel(f)).join(' · ');
  const groups = [filled];
  if (fix) groups.push(correct ? `⟳ ${fix}` : `(⟳ ${fix} ignoré)`);
  return groups.filter(g => !!g).join('   ');
}

export function sourceText(r, acoustIdAvailable) {
  if (r.state === 'found') return r.proposal.source;
  if (r.state === 'pending') return '';
  return r.file.coreMissing ? 'Deezer + MusicBrainz' + (acoustIdAvailable ? ' + AcoustID' : '') : 'Deezer';
}

/** Ligne d'état : avancement pendant la recherche, bilan ensuite. */
export function reviewStateText({ rows, running, stopping, examined, total }) {
  const foundRows = rows.filter(r => r.state === 'found');
  const sure = foundRows.filter(r => r.sure).length;
  if (running) {
    return stopping
      ? `Arrêt de la recherche… ${examined} / ${total} fichiers examinés`
      : `Recherche en cours… ${examined} / ${total} fichiers`;
  }
  let text = `${sure} correspondance(s) sûre(s) · ${foundRows.length - sure} probable(s) · ${examined - foundRows.length} sans réponse`;
  if (examined < total) text += ` · ${total - examined} non examiné(s) — recherche arrêtée`;
  return text;
}

/** Tags à écrire pour une ligne cochée : champs vides comblés, plus les champs contredits si demandé. */
export function tagsOf(r, correct) {
  const all = [...r.fills, ...(correct ? r.corrects : [])];
  return all.map(([field, value]) => ({ field, value }));
}

/** Question posée avant l'écriture ; `left` : fichiers que la recherche, encore en cours, n'a pas interrogés. */
export function writeQuestion(chosen, correct, left) {
  const fieldCount = chosen.reduce((n, r) => n + tagsOf(r, correct).length, 0);
  const replaced = correct ? chosen.reduce((n, r) => n + r.corrects.length, 0) : 0;
  let message = `Écrire ${fieldCount} tag(s) dans ${chosen.length} fichier(s) ?`;
  if (left > 0)
    message += `\nLa recherche n'a pas parcouru toute la liste : ${left} fichier(s) n'ont pas été interrogés et resteront tels quels.`;
  message +=
    replaced > 0
      ? `\nDont ${replaced} tag(s) existant(s) remplacé(s) par une valeur différente — l'ancienne valeur sera perdue.`
      : `\nSeuls des champs vides sont renseignés ; aucun tag existant n'est remplacé.`;
  return message;
}

/** Fichiers à écrire, tels que le serveur les attend. */
export function writeItems(chosen, correct) {
  return chosen.map(r => ({
    path: r.file.dbPath,
    fields: tagsOf(r, correct),
    overwriteExisting: correct && r.corrects.length > 0,
  }));
}
