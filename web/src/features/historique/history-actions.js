import { api } from '../../core/api/api';
import { player } from '../../core/player/player';
import { notify } from '../../core/ui/notify';
import { confirm } from '../../shared/dialog/confirm';
import { openSearchDialog } from '../musique/search-dialog/open-search-dialog';
import { ADD_PENDING, CLEAR, CLEAR_SEARCHES, DELETE_SEARCH, PURGE } from './history-api';
import { playerTrackOf, toPendingTracks } from './history-tracks';

// ---------------------------------------------------------------- recherches

/**
 * « Relancer » : ouvre la fenêtre de recherche avec la requête ; les pistes choisies
 * rejoignent la liste de la page Musique.
 * @param {string} query
 * @param {(to: string) => unknown} navigate
 */
export async function relaunchSearch(query, navigate) {
  await navigate('/musique');
  const result = await openSearchDialog(query);
  const tracks = result?.tracks ?? [];
  if (!tracks.length) return;
  const input = toPendingTracks(tracks);
  const r = await api.mutate(ADD_PENDING, { tracks: input }, { label: `Ajouter ${input.length} titre(s) à la liste` });
  if (r.error) notify.error(r.error);
  else if (r.queued) notify.info('Ajout mis en attente : il partira au retour de la connexion.');
}

export async function deleteSearch(query) {
  const r = await api.mutate(DELETE_SEARCH, { query }, { label: 'Supprimer une recherche', refetch: ['History'] });
  if (r.error) notify.error(r.error);
}

export async function clearSearches() {
  const ok = await confirm({
    title: 'Vider les recherches',
    message: "Retirer toutes les recherches de l'historique ?",
    confirm: 'Vider',
    danger: true,
  });
  if (!ok) return;
  const r = await api.mutate(CLEAR_SEARCHES, {}, { label: 'Vider les recherches', refetch: ['History'] });
  if (r.error) notify.error(r.error);
}

// ---------------------------------------------------------------- téléchargements

/** La file est tout l'historique (entrées présentes sur le disque), départ sur la ligne cliquée. */
export function playDownload(e, shown) {
  if (!e.onDisk) {
    notify.info("Ce fichier n'est plus sur le disque : il n'y a rien à écouter.");
    return;
  }
  player.play(
    playerTrackOf(e),
    shown.filter(x => x.onDisk).map(x => playerTrackOf(x)),
  );
}

export async function purgeMissing() {
  const r = await api.mutate(PURGE, {}, { label: 'Purger les fichiers introuvables', refetch: ['History'] });
  if (r.error) notify.error(r.error);
  else if (r.data) notify.info(r.data.purgeMissingHistory.message);
}

/** « Vider l'historique » demande confirmation ; les fichiers du disque sont conservés. */
export async function clearHistory() {
  const ok = await confirm({
    title: "Vider l'historique",
    message: "Retirer toutes les entrées de l'historique des téléchargements ?\nLes fichiers du disque sont conservés.",
    confirm: 'Vider',
    danger: true,
  });
  if (!ok) return;
  const r = await api.mutate(CLEAR, {}, { label: "Vider l'historique", refetch: ['History'] });
  if (r.error) notify.error(r.error);
  else if (r.data) notify.info(r.data.clearHistory.message);
}
