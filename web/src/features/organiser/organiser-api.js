import { api } from '../../core/api/api';
import { checksToMap } from './organize-checks';
import {
  APPLY,
  CHECKS,
  DUPLICATES,
  ENTRIES,
  LOOKUP,
  NAME_TAGS,
  PLAN,
  PREVIEW,
  RESET_CHECKS,
  SAVE_OPTIONS,
  SET_CHECKS,
  START,
  STATE,
} from './organiser.queries';

/**
 * Appels serveur de la page Organiser (routes déclarées dans organiser.queries.js).
 * Les requêtes rendent la donnée utile ; les mutations rendent le résultat de `api.mutate`.
 */

/** État enregistré : session d'analyse, dossier par défaut, options. */
export async function fetchOrganizeState() {
  return (await api.query(STATE)).organizeState;
}

/** Balises du motif de nommage (catégories du menu « Insérer une balise », motifs proposés). */
export async function fetchNameTags() {
  return (await api.query(NAME_TAGS)).organizeNameTags;
}

/** Fichiers lus par l'analyse. */
export async function fetchEntries(sessionId) {
  return (await api.query(ENTRIES, { sessionId })).organizeEntries.items;
}

export async function fetchPlan(sessionId, options) {
  return (await api.query(PLAN, { sessionId, options })).organizePlan;
}

/** Cases posées à la main (index → coché). */
export async function fetchTouched(sessionId) {
  return checksToMap((await api.query(CHECKS, { sessionId })).organizeChecks);
}

/** Aperçu du nom d'un fichier selon le motif. */
export async function fetchPreview(sessionId, pattern, rename) {
  return (await api.query(PREVIEW, { sessionId, pattern, rename })).organizePreview;
}

export const startAnalysis = root => api.mutate(START, { root }, { label: 'Analyser un dossier', queueable: false });

export const saveOptions = options =>
  api.mutate(SAVE_OPTIONS, { options }, { label: 'Enregistrer les options', queueable: true });

export const saveChecks = (sessionId, indices, value) =>
  api.mutate(SET_CHECKS, { sessionId, indices, value }, { label: 'Cocher des fichiers', queueable: false });

export const resetChecks = sessionId =>
  api.mutate(RESET_CHECKS, { sessionId }, { label: 'Cocher ce qui est à faire', queueable: false });

export const applyOrganize = (sessionId, indices, options) =>
  api.mutate(APPLY, { sessionId, indices, options }, { label: 'Ranger la bibliothèque', queueable: false });

export const findDuplicates = sessionId =>
  api.mutate(DUPLICATES, { sessionId }, { label: 'Rechercher les doublons', queueable: false });

export const startLookup = (sessionId, indices) =>
  api.mutate(LOOKUP, { sessionId, indices }, { label: 'Compléter les tags', queueable: false });
