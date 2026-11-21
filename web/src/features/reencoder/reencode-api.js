import { api } from '../../core/api/api';
import { endpoint } from '../../core/api/endpoints';

export const EXPORT = endpoint('ExportOptions', 'exportOptions', 'GET /api/downloads/export-options');
export const SCAN = endpoint('ScanLocalFiles', 'scanLocalFiles', 'POST /api/library/scan-files');
export const MATCH = endpoint('BestMatch', 'bestMatch', 'GET /api/catalog/best-match');
export const CONVERT = endpoint('EnqueueConversions', 'enqueueConversions', 'POST /api/downloads/conversions');

/** Format et qualité cibles définis dans les Paramètres (null s'ils manquent). */
export async function loadExportTarget() {
  const d = await api.query(EXPORT);
  return d.exportOptions?.current ?? null;
}

/** Lance l'analyse des fichiers du serveur ; rend l'identifiant de la tâche. */
export async function scanLocalFiles(paths) {
  const r = await api.mutate(SCAN, { paths }, { label: 'Lire les fichiers à réencoder', queueable: false });
  const id = r.data?.scanLocalFiles?.id;
  if (!id) throw new Error(r.error ?? 'Analyse impossible');
  return id;
}

/** Meilleure correspondance Deezer d'un titre (null si rien, ou en cas d'échec). */
export async function findBestMatch(artist, title) {
  try {
    const data = await api.query(MATCH, { artist, title }, true);
    return data.bestMatch ?? null;
  } catch {
    return null;
  }
}

/** Met en file la conversion des fichiers ; rend les tâches créées, dans l'ordre des fichiers. */
export async function enqueueConversions(items, batchId) {
  const r = await api.mutate(
    CONVERT,
    {
      items: items.map(i => ({ localPath: i.path, deezerId: i.match?.id ?? null })),
      batchId,
    },
    { label: `Convertir ${items.length} fichier(s)`, queueable: false },
  );
  const created = r.data?.enqueueConversions;
  if (!created) throw new Error(r.error ?? 'Conversion impossible');
  return created;
}
