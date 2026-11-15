import { isActive } from '../../core/jobs/jobs';

/** Comptes de la ligne d'état : fichiers lus, titres douteux, illisibles, références attendues. */
export function qualityStats(rows) {
  const all = [...rows.values()];
  return {
    count: all.length,
    flagged: all.filter(r => r.defects.length > 0).length,
    errors: all.filter(r => r.error).length,
    waiting: all.filter(r => r.referenceState === 'pending' || r.referenceState === 'network').length,
  };
}

/** Avancement de l'analyse (null = indéterminé ou aucune analyse en cours). */
export function scanProgress(job) {
  if (!job || !isActive(job)) return null;
  return job.total > 0 ? Math.min(1, (job.done + job.failed) / job.total) : null;
}

/** Ligne d'état sous le tableau : analyse en cours, ou bilan de la dernière. */
export function statusLine(job, stats, loaded, thresholds) {
  if (job && isActive(job))
    return job.status === 'QUEUED' ? 'Analyse en attente…' : (job.message ?? 'Analyse en cours…');
  const s = stats;
  if (s.count === 0) return loaded ? "Lancez l'analyse pour repérer les titres à la qualité médiocre." : 'Chargement…';
  const parts = [
    `${job?.status === 'CANCELLED' ? 'Analyse interrompue' : 'Analyse terminée'} : ${s.count} fichier(s) lus, ${s.flagged} titre(s) à la qualité douteuse`,
  ];
  if (s.errors) parts.push(`${s.errors} illisible(s)`);
  if (s.waiting) parts.push(`durée du morceau en cours de recherche pour ${s.waiting} titre(s)`);
  if (thresholds && !thresholds.deep) parts.push('craquements non mesurés (analyse rapide)');
  return parts.join(' — ') + '.';
}

/** Retéléchargements en cours : une barre globale, la colonne Statut pouvant être hors écran. */
export function redownloadSummary(redownloadJobs) {
  const active = redownloadJobs.filter(isActive);
  if (active.length === 0) return null;
  const all = redownloadJobs.filter(j => active.some(a => a.groupKey && a.groupKey === j.groupKey) || isActive(j));
  const finished = all.length - active.length;
  const running = active.find(j => j.status !== 'QUEUED');
  const value = (finished + active.reduce((s, j) => s + (j.status === 'QUEUED' ? 0 : j.progress), 0)) / all.length;
  const detail = running ? `${running.title} — ${running.message ?? 'Initialisation…'}` : 'En attente…';
  return { value: Math.min(1, value), label: `Retéléchargement ${finished + 1} / ${all.length} : ${detail}` };
}

/** Titres cochés à retélécharger : sans les illisibles ; ceux sans artiste ou titre sont écartés. */
export function redownloadPlan(list, selected) {
  const chosen = list.filter(r => selected.has(r.path) && !r.error);
  const targets = chosen.filter(r => (r.artist ?? '').trim() && (r.title ?? '').trim());
  return { chosen: chosen.length, targets, skipped: chosen.length - targets.length };
}

/** Question posée avant de retélécharger. */
export function redownloadMessage(count, skipped) {
  return (
    `${count} titre(s) vont être réextraits et remplaceront la version abîmée.\n\n` +
    "Le fichier produit est rangé selon les règles de l'écran « Organiser » (sous-dossiers, modèle de nom, « Divers ») : un titre mal placé peut donc changer de dossier au passage.\n\n" +
    'Chaque titre est ensuite réanalysé pour vérifier que le défaut a disparu.' +
    (skipped
      ? `\n\n${skipped} titre(s) coché(s) sont écartés : leurs tags ne donnent pas l'artiste ou le titre nécessaires à la recherche.`
      : '') +
    '\n\nContinuer ?'
  );
}
