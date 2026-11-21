import { useState } from 'react';
import { jobs } from '../../../core/jobs/jobs';
import { notify } from '../../../core/ui/notify';
import { addPending, enqueueDownloads, purgeDownloads, removeJobs, removePending, retryDownloads } from '../music-api';
import { toInput } from '../track-input';
import { openSearchDialog } from '../search-dialog/open-search-dialog';

/**
 * Actions de la barre et des lignes de la file. Chaque action recharge la file
 * ensuite ; `purged` compte les téléchargements réussis retirés par « Purger »
 * (ils restent dans le total « fichiers téléchargés »).
 *
 * @param {{ rows: object[], rowsRef: { current: object[] }, eligible: object[], failedRows: object[],
 *   selected: Set<string>, clearSelection: () => void, reload: () => Promise<void> }} p
 */
export function useQueueActions({ rows, rowsRef, eligible, failedRows, selected, clearSelection, reload }) {
  const [purged, setPurged] = useState(0);

  const search = async () => {
    const result = await openSearchDialog();
    const tracks = result?.tracks ?? [];
    if (!tracks.length) return;
    const known = new Set(rowsRef.current.map(r => r.track.id));
    const fresh = tracks.map(toInput).filter(t => t.id && !known.has(t.id));
    const skipped = tracks.length - fresh.length;
    if (fresh.length) {
      const r = await addPending(fresh);
      if (r.error) notify.error(r.error);
      else if (r.queued) notify.info('Ajout mis en attente : il partira au retour de la connexion.');
      await reload();
    }
    if (skipped) notify.info(`${skipped} titre(s) déjà dans la liste n'ont pas été ajoutés une seconde fois.`);
  };

  const retryJobs = async list => {
    if (!list.length) return;
    const r = await retryDownloads(list.map(x => x.id));
    if (r.error) notify.error(r.error);
  };

  const extract = async () => {
    const list = eligible;
    const pendingRows = list.filter(r => r.kind === 'pending');
    const failed = list.filter(r => r.kind === 'job');
    if (pendingRows.length) {
      const r = await enqueueDownloads(
        pendingRows.map(p => toInput(p.track)),
        pendingRows.map(p => p.id),
      );
      if (r.error) notify.error(r.error);
      else if (r.data?.enqueueDownloads) {
        // Un titre déjà présent (ou en cours pour quelqu'un d'autre) n'est pas retéléchargé : il rejoint vos titres.
        const shared = pendingRows.length - r.data.enqueueDownloads.length;
        if (shared > 0)
          notify.info(
            `${shared} titre(s) déjà dans la bibliothèque : ajouté(s) à vos titres sans nouveau téléchargement.`,
          );
      }
    }
    if (failed.length) await retryJobs(failed);
    clearSelection();
    await reload();
  };

  const retry = async () => {
    await retryJobs(failedRows);
    await reload();
  };

  const cancel = async r => {
    if (r.job) await jobs.cancel({ service: 'downloader', id: r.id });
  };

  const removeSelection = async () => {
    const ids = [...selected];
    const list = rows.filter(r => ids.includes(r.id));
    const pend = list.filter(r => r.kind === 'pending').map(r => r.id);
    const jobIds = list.filter(r => r.kind === 'job').map(r => r.id);
    if (!list.length) return;
    if (pend.length) await removePending(pend);
    if (jobIds.length) await removeJobs(jobIds);
    jobs.forget(jobIds);
    setPurged(0);
    clearSelection();
    notify.info(`${list.length} élément(s) retiré(s) de votre liste.`);
    await reload();
  };

  const purge = async () => {
    const done = rows.filter(r => r.succeeded).length;
    if (!done) {
      notify.info('Aucun téléchargement réussi à purger.');
      return;
    }
    const r = await purgeDownloads();
    if (r.error) {
      notify.error(r.error);
      return;
    }
    setPurged(n => n + done);
    notify.info(`${r.data?.purgeDownloads ?? done} téléchargement(s) réussi(s) retiré(s) de la liste.`);
    await reload();
  };

  return { purged, search, extract, retry, cancel, removeSelection, purge };
}
